# Blog Studio

테마를 골라 블로그 글을 AI로 작성하고, 미리보기로 확인한 뒤 네이버 블로그·티스토리에 붙여넣는 도구입니다.
웹앱(휴대폰·크롬), Windows 앱, Mac 앱이 같은 계정과 데이터를 함께 씁니다.

| 영역 | 기술 |
|---|---|
| 웹앱 | React + TypeScript + Vite (PWA) |
| Windows 앱 | C# .NET 8, WPF, WebView2 |
| Mac 앱 | C# .NET 8, Avalonia |
| 백엔드 | Supabase (Auth, Postgres, Vault, Edge Functions, Realtime) |
| AI | Claude · OpenAI · Gemini — API 키 또는 내 PC의 공식 CLI(구독) |
| PC 앱 업데이트 | Velopack + GitHub Releases |

## 폴더

```
apps/web                      웹앱
apps/desktop/BlogStudio.*     Windows(WPF) · Mac(Avalonia) 앱
src/BlogStudio.*              PC 앱 공용 C# (Core, Presentation, Tests)
shared/                       테마 팩 · 공통 프롬프트 · 스키마 · 스타일 (모든 앱이 같이 읽음)
supabase/                     DB 마이그레이션, Edge Functions
tools/python-bot              초기 프로토타입 (지금 매일 쓰는 파이썬 봇)
docs/                         설계 문서
```

## 생성 경로

- **기본: Claude Code (Claude Pro 구독, 추가 요금 없음)** — PC 앱의 로컬 워커가 내 PC에 설치된 Claude Code를 실행합니다.
  웹·휴대폰에서 요청하면 대기열에 들어가고, 켜져 있는 PC가 처리합니다. PC가 꺼져 있으면 켤 때까지 기다립니다.
  Claude 구독 로그인은 Anthropic 약관상 Claude Code·claude.ai 전용이므로, 앱은 토큰을 읽거나 옮기지 않고 본인 PC에서 본인 용도로만 실행합니다.
- **선택: API 키** — Gemini 무료 키(예비) 또는 유료 키. Supabase Vault에 암호화 저장, Edge Function 안에서만 복호화합니다.
  다른 사람에게 공개할 때는 각자 자기 API 키를 연결합니다.

## 시작하기

1. Supabase 프로젝트 생성 → `supabase link --project-ref <id>` → `supabase db push`
2. `apps/web/.env.local`에 Supabase URL과 publishable 키 입력
3. 각 앱 폴더의 README 참고

## 개발 단계

0 뼈대 → 1 Windows 앱 + 로컬 워커(Claude Code) → 2 웹 MVP → 3 배포·업데이트 → 4 테마 확장 → 5 Mac 앱 → 6 예비 경로(API 키)·토큰 고도화

## 커밋·배포 규칙

- 브랜치: `main`(항상 동작), `feat/*`, `fix/*` → PR로 합침
- 커밋: `feat(web): ...`, `fix(desktop): ...`
- PC 앱 릴리스: `git tag desktop-vX.Y.Z && git push --tags`
