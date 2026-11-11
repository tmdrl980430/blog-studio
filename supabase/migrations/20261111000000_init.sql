-- 블로그 스튜디오 초기 스키마
-- 모든 테이블은 RLS로 "내 행만" 접근. API 키 원문은 Vault에만 저장.

create extension if not exists "pgcrypto";

-- ───────── 회원 정보 ─────────
create table public.profiles (
  id              uuid primary key references auth.users(id) on delete cascade,
  nickname        text,
  avatar_url      text,
  naver_blog_id   text,
  tistory_name    text,
  default_route   text not null default 'subscription_first'  -- 기본: Claude Code(Pro), 예비: API 키
                  check (default_route in ('subscription_first', 'api_only', 'subscription_only')),
  created_at      timestamptz not null default now(),
  updated_at      timestamptz not null default now()
);

-- 가입 시 프로필 자동 생성
create function public.handle_new_user() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles (id, nickname, avatar_url)
  values (new.id,
          coalesce(new.raw_user_meta_data->>'name', split_part(new.email, '@', 1)),
          new.raw_user_meta_data->>'avatar_url');
  return new;
end $$;
create trigger on_auth_user_created after insert on auth.users
  for each row execute function public.handle_new_user();

-- ───────── 테마 / 채널 ─────────
-- owner_id가 null이면 기본 제공 테마(모두 읽기 가능), 값이 있으면 내 테마
create table public.themes (
  id          uuid primary key default gen_random_uuid(),
  owner_id    uuid references auth.users(id) on delete cascade,
  slug        text not null,
  name        text not null,
  pack        jsonb not null,          -- theme.yaml + inputs.json + prompt.md 내용
  version     int  not null default 1,
  created_at  timestamptz not null default now(),
  unique (owner_id, slug)
);

create table public.channels (
  id            uuid primary key default gen_random_uuid(),
  owner_id      uuid not null references auth.users(id) on delete cascade,
  theme_id      uuid not null references public.themes(id),
  name          text not null,
  schedule      jsonb not null default '{}'::jsonb,  -- 요일별 트랙 등
  settings      jsonb not null default '{}'::jsonb,  -- 말투, 분량, 연결 우선순위 등
  created_at    timestamptz not null default now()
);

-- 기술 테마의 커리큘럼 진행, 리뷰 테마의 대기 목록 등 "다음에 쓸 것"
create table public.topic_queue (
  id          uuid primary key default gen_random_uuid(),
  owner_id    uuid not null references auth.users(id) on delete cascade,
  channel_id  uuid not null references public.channels(id) on delete cascade,
  position    int  not null,
  title       text not null,
  inputs      jsonb not null default '{}'::jsonb,   -- 테마 입력 폼 값 (제품명, 메모 등)
  status      text not null default 'pending' check (status in ('pending', 'used', 'skipped')),
  created_at  timestamptz not null default now()
);

-- ───────── 글 ─────────
create table public.posts (
  id          uuid primary key default gen_random_uuid(),
  owner_id    uuid not null references auth.users(id) on delete cascade,
  channel_id  uuid not null references public.channels(id) on delete cascade,
  topic_id    uuid references public.topic_queue(id),
  title       text not null,
  summary     text,
  tags        text[] not null default '{}',
  body_md     text not null,                 -- AI 출력은 마크다운 한 벌만 저장
  status      text not null default 'draft' check (status in ('draft', 'published')),
  published   jsonb not null default '{}'::jsonb, -- {"naver": "url", "tistory": "url"}
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

-- ───────── AI 연결 ─────────
create table public.provider_connections (
  id             uuid primary key default gen_random_uuid(),
  owner_id       uuid not null references auth.users(id) on delete cascade,
  provider       text not null check (provider in ('claude', 'openai', 'gemini')),
  kind           text not null check (kind in ('api_key', 'local_cli')),
  label          text not null,
  vault_secret_id uuid,            -- api_key일 때만. 원문은 vault.secrets에
  key_last4      text,
  default_model  text,
  monthly_budget_usd numeric(10,2),
  status         text not null default 'ok' check (status in ('ok', 'limit', 'error')),
  priority       int  not null default 100,
  created_at     timestamptz not null default now()
);

-- API 키 저장: 클라이언트는 이 함수만 호출. 원문은 다시 읽을 수 없음.
create function public.save_api_key(p_provider text, p_label text, p_key text, p_model text default null)
returns uuid language plpgsql security definer set search_path = public, vault as $$
declare v_secret uuid; v_id uuid;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  v_secret := vault.create_secret(p_key, 'apikey:' || auth.uid() || ':' || gen_random_uuid());
  insert into public.provider_connections (owner_id, provider, kind, label, vault_secret_id, key_last4, default_model)
  values (auth.uid(), p_provider, 'api_key', p_label, v_secret, right(p_key, 4), p_model)
  returning id into v_id;
  return v_id;
end $$;
revoke all on function public.save_api_key from public, anon;
grant execute on function public.save_api_key to authenticated;

-- ───────── 생성 작업 큐 (웹 → PC 구독 워커) ─────────
create table public.generation_jobs (
  id            uuid primary key default gen_random_uuid(),
  owner_id      uuid not null references auth.users(id) on delete cascade,
  channel_id    uuid not null references public.channels(id) on delete cascade,
  topic_id      uuid references public.topic_queue(id),
  route         text not null check (route in ('subscription', 'api', 'batch')),
  scope         text not null default 'full' check (scope in ('full', 'section', 'meta')),
  section_hint  text,                              -- 부분 재생성 대상 소제목
  status        text not null default 'queued' check (status in ('queued', 'running', 'done', 'failed')),
  claimed_by    text,                              -- 처리한 기기 이름
  post_id       uuid references public.posts(id),
  error         text,
  created_at    timestamptz not null default now(),
  finished_at   timestamptz
);

create table public.usage_log (
  id             bigint generated always as identity primary key,
  owner_id       uuid not null references auth.users(id) on delete cascade,
  connection_id  uuid references public.provider_connections(id) on delete set null,
  job_id         uuid references public.generation_jobs(id) on delete set null,
  model          text,
  input_tokens   int,
  output_tokens  int,
  cached_tokens  int,
  cost_usd_est   numeric(10,5),
  created_at     timestamptz not null default now()
);

-- ───────── 앱 설정 (최소 지원 버전 등) ─────────
create table public.app_config (
  key   text primary key,
  value jsonb not null
);
insert into public.app_config values ('min_desktop_version', '"1.0.0"');

-- ───────── RLS ─────────
alter table public.profiles             enable row level security;
alter table public.themes               enable row level security;
alter table public.channels             enable row level security;
alter table public.topic_queue          enable row level security;
alter table public.posts                enable row level security;
alter table public.provider_connections enable row level security;
alter table public.generation_jobs      enable row level security;
alter table public.usage_log            enable row level security;
alter table public.app_config           enable row level security;

create policy "own profile"  on public.profiles for all using (id = auth.uid()) with check (id = auth.uid());
create policy "read themes"  on public.themes   for select using (owner_id is null or owner_id = auth.uid());
create policy "own themes"   on public.themes   for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());
create policy "own channels" on public.channels for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());
create policy "own topics"   on public.topic_queue for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());
create policy "own posts"    on public.posts    for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());
create policy "own conns"    on public.provider_connections for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());
create policy "own jobs"     on public.generation_jobs for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());
create policy "own usage"    on public.usage_log for select using (owner_id = auth.uid());
create policy "read config"  on public.app_config for select using (true);

-- PC 워커가 새 작업을 바로 받도록 Realtime 발행
alter publication supabase_realtime add table public.generation_jobs;
