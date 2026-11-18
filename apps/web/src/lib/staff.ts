import { DEFAULT_CATEGORIES, categoryForTrack, formatCategory, matchCategory, type BlogCategory } from "./categories";
import { cleanBasePrompt, type ThemePack, type Track } from "./themes";
import { HISTORY_LIMIT, type HistoryLine, type PromptPair } from "./prompt";

/**
 * 앱 안 AI 직원 (shared/staff/*.md). 데스크톱 StaffPrompts와 같은 프롬프트 조립·응답 정리.
 * 작가는 prompt.ts(buildPrompt)가 맡는다.
 */
export interface TopicIdea {
  topic: string;
  keyword: string;
  reason: string;
  /** 목록 안의 "상위 > 하위" 또는 "" (트랙 기본값을 씀) */
  category: string;
}

export interface EditorReview {
  verdict: string;
  notes: string[];
}

/** 성과 분석가에게 보내는 글 기록 한 줄에 쓰는 값. */
export interface StatsPost {
  date: string; // yyyy-MM-dd
  track: string;
  publishedTo?: string[];
}

export interface BlogStats {
  total: number;
  thisMonth: number;
  thisWeek: number;
  last30Days: number;
  streakDays: number;
  bestStreak: number;
  publishedNaver: number;
  publishedTistory: number;
  unpublished: number;
}

export interface TrackRecord {
  key: string;
  name: string;
  posts: number;
  published: number;
  lastDate: string | null;
}

export interface TrackPriority {
  trackKey: string;
  trackName: string;
  direction: string;
  reason: string;
}

export const IDEA_COUNT = 5;
export const TOPIC_MAX = 60;
export const NOTE_MAX = 7;
export const PUBLISHABLE = "발행 가능";
export const NEEDS_EDIT = "수정 후 발행";
export const REWRITE = "다시 쓰기";
export const PRIORITY_MAX = 3;

const FALLBACK_PLANNER =
  '당신은 한국어 블로그의 주제 기획자입니다. 검색될 만한 주제 5개를 "주제 | 핵심 검색 키워드 | 이유 | 카테고리" 형식으로 한 줄에 하나씩만 출력합니다. 카테고리는 [카테고리 목록]에서 그대로 하나 고릅니다.';
const FALLBACK_EDITOR =
  '당신은 한국어 블로그 글의 편집자입니다. 첫 줄에 "판정: 발행 가능 | 수정 후 발행 | 다시 쓰기" 중 하나, 다음 줄부터 "- 고칠 점"을 최대 7개 출력합니다.';
const FALLBACK_ANALYST =
  '당신은 한국어 블로그의 성과 분석가입니다. [트랙별 기록]의 숫자만 근거로, 다음에 글을 더 쓸 트랙을 우선순위 순서로 최대 3줄 "트랙 | 다음 주제 방향 | 이유" 형식으로만 출력합니다. 트랙 이름은 목록에 있는 그대로 씁니다.';

const LIST_MARK = /^(\d+[.)]\s*|[-*•]\s*)/;

/** 기획자: 트랙 안에서 오늘 쓸 주제 후보. 이전 글은 같은 트랙 최근 15편만. */
export function buildTopicIdeas(
  staffPrompt: string,
  theme: ThemePack,
  track: Track,
  history: HistoryLine[],
  hint = "",
  categories: readonly BlogCategory[] = DEFAULT_CATEGORIES,
): PromptPair {
  const recent = history
    .filter((h) => h.track === track.key)
    .sort((a, b) => b.date.localeCompare(a.date))
    .slice(0, HISTORY_LIMIT)
    .map((h) => "- " + h.title);
  const upcoming = track.topics.slice(0, 10).map((t) => "- " + t);
  const trackDefault = categoryForTrack(track.key, categories);
  let user =
    `[블로그] ${theme.name}\n[트랙] ${track.name}\n[트랙 작성 가이드] ${track.guide.trim()}` +
    `\n\n[트랙의 기본 주제 목록 (참고, 그대로 베끼지 않음)]\n${upcoming.length ? upcoming.join("\n") : "(없음)"}` +
    `\n\n[이전 글]\n${recent.length ? recent.join("\n") : "(없음 — 첫 글)"}` +
    `\n\n[카테고리 목록]\n${categories.map((c) => "- " + formatCategory(c)).join("\n")}` +
    `\n[이 트랙 기본 카테고리] ${trackDefault ? formatCategory(trackDefault) : "(없음 — 목록에서 가장 가까운 것)"}`;
  if (hint.trim()) user += `\n\n[요청] ${hint.trim()}`;
  return { system: staffPrompt.trim() || FALLBACK_PLANNER, user };
}

/** "주제 | 키워드 | 이유 | 카테고리" 줄을 읽는다. 번호·따옴표 제거, 너무 긴 주제 제외, 중복 제거, 최대 5개. */
export function parseTopicIdeas(response: string, categories: readonly BlogCategory[] = DEFAULT_CATEGORIES): TopicIdea[] {
  const out: TopicIdea[] = [];
  for (const raw of response.split("\n")) {
    const line = raw.trim().replace(LIST_MARK, "").trim();
    if (!line || line.startsWith("주제 |")) continue;
    const p = line.split("|").map((x) => x.trim().replace(/^["“”']+|["“”']+$/g, "").trim());
    if (!p[0] || p[0].length > TOPIC_MAX || out.some((i) => i.topic === p[0])) continue;
    const c = p.length > 3 ? matchCategory(p[3], categories) : null;
    out.push({ topic: p[0], keyword: p[1] ?? "", reason: p[2] ?? "", category: c ? formatCategory(c) : "" });
    if (out.length === IDEA_COUNT) break;
  }
  return out;
}

/** 편집자: 저장된 글 전체(머리말 포함)를 보고 검수 의견. */
export const buildEditorReview = (staffPrompt: string, postFileText: string, author: string): PromptPair => ({
  system: (staffPrompt.trim() || FALLBACK_EDITOR).replaceAll("{author}", author.trim() || "글쓴이"),
  user: "[검수할 글]\n" + postFileText,
});

/** "판정: …" 줄과 "- 고칠 점" 줄을 읽는다. 판정이 없으면 고칠 점 유무로 정한다. */
export function parseEditorReview(response: string): EditorReview {
  let verdict: string | null = null;
  let notes: string[] = [];
  for (const raw of response.split("\n")) {
    const line = raw.trim().replace(/^\*+|\*+$/g, "").trim();
    if (!line) continue;
    if (line.startsWith("판정")) {
      // 형식 안내를 그대로 따라 쓴 줄처럼 둘 이상 들어 있으면 판정으로 보지 않음
      const found = [REWRITE, NEEDS_EDIT, PUBLISHABLE].filter((v) => line.includes(v));
      if (found.length === 1) verdict ??= found[0];
      continue;
    }
    if (!LIST_MARK.test(line)) continue;
    const note = line.replace(LIST_MARK, "").trim();
    if (note && !notes.includes(note)) notes.push(note);
  }
  notes = notes.slice(0, NOTE_MAX);
  if (verdict === PUBLISHABLE && notes.length > 0) verdict = NEEDS_EDIT;
  return { verdict: verdict ?? (notes.length ? NEEDS_EDIT : PUBLISHABLE), notes };
}

/** 편집자 의견을 "Claude로 고치기" 요청문으로. */
export const revisionFromReview = (review: EditorReview) =>
  "편집자 검수 의견대로 고쳐 주세요. 의견과 관계없는 부분은 그대로 둡니다.\n" + review.notes.map((n) => "- " + n).join("\n");

/** 기획자 주제로 쓸 때 작가에게 덧붙이는 요청 (데스크톱과 같은 문장). */
export const keywordRequest = (extra: string, keyword: string) =>
  keyword.trim() ? `${extra}\n핵심 검색 키워드 "${keyword.trim()}"를 제목 앞쪽과 첫 문단에 자연스럽게 넣어 주세요.`.trim() : extra;

// ── 성과 분석가 (데스크톱 BlogStats·AnalystPrompts와 같은 규칙) ──
const dayNumber = (iso: string) => {
  const [y, m, d] = iso.split("-").map(Number);
  return Date.UTC(y, m - 1, d) / 86_400_000;
};
const isPublished = (p: StatsPost) => (p.publishedTo?.length ?? 0) > 0;

/** 글 기록으로 보는 성과. 조회수는 앱에 없으므로 "쓰고 올린 것"만. */
export function blogStats(posts: readonly StatsPost[], today: string): BlogStats {
  const now = dayNumber(today);
  const nums = posts.map((p) => dayNumber(p.date));
  const days = new Set(nums.filter(Number.isFinite));
  const monday = now - ((new Date(now * 86_400_000).getUTCDay() + 6) % 7);

  // 연속 기록: 오늘 아직 안 썼으면 어제부터 센다
  let streakDays = 0;
  for (let d = days.has(now) ? now : now - 1; days.has(d); d--) streakDays++;

  let bestStreak = 0;
  let run = 0;
  let prev: number | null = null;
  for (const d of [...days].sort((a, b) => a - b)) {
    run = prev !== null && prev + 1 === d ? run + 1 : 1;
    bestStreak = Math.max(bestStreak, run);
    prev = d;
  }

  return {
    total: posts.length,
    thisMonth: posts.filter((p) => p.date.slice(0, 7) === today.slice(0, 7)).length,
    thisWeek: nums.filter((d) => d >= monday && d <= now).length,
    last30Days: nums.filter((d) => d > now - 30 && d <= now).length,
    streakDays,
    bestStreak,
    publishedNaver: posts.filter((p) => p.publishedTo?.includes("naver")).length,
    publishedTistory: posts.filter((p) => p.publishedTo?.includes("tistory")).length,
    unpublished: posts.filter((p) => !isPublished(p)).length,
  };
}

/** 트랙별 글 수·올림 수·마지막 글 날짜. 글이 없는 트랙도 0편으로 넣고, 목록에 없는 트랙의 글은 세지 않는다. */
export function trackRecords(posts: readonly StatsPost[], tracks: readonly { key: string; name: string }[]): TrackRecord[] {
  const seen = new Set<string>();
  return tracks.filter((t) => !seen.has(t.key) && seen.add(t.key)).map((t) => {
    const list = posts.filter((p) => p.track === t.key);
    return {
      key: t.key,
      name: t.name,
      posts: list.length,
      published: list.filter(isPublished).length,
      lastDate: list.length ? list.map((p) => p.date).sort().at(-1)! : null,
    };
  });
}

function recordLine(t: TrackRecord, today: string) {
  if (!t.lastDate) return `${t.name}: 아직 글 없음`;
  const ago = dayNumber(today) - dayNumber(t.lastDate);
  return `${t.name}: 글 ${t.posts}편 · 올림 ${t.published}편 · 마지막 글 ${ago <= 0 ? "오늘" : `${ago}일 전`}`;
}

export function buildAnalystPrompt(staffPrompt: string, stats: BlogStats, tracks: readonly TrackRecord[], today: string, hint = ""): PromptPair {
  let user =
    `[오늘] ${today}` +
    `\n[전체] 글 ${stats.total}편 · 이번 주 ${stats.thisWeek}편 · 이번 달 ${stats.thisMonth}편 · 최근 30일 ${stats.last30Days}편 · 연속 ${stats.streakDays}일 (최고 ${stats.bestStreak}일)` +
    `\n[올림] 네이버 ${stats.publishedNaver}편 · 티스토리 ${stats.publishedTistory}편 · 아직 안 올린 글 ${stats.unpublished}편` +
    `\n\n[트랙별 기록]\n${tracks.length ? tracks.map((t) => "- " + recordLine(t, today)).join("\n") : "(없음)"}`;
  if (hint.trim()) user += `\n\n[요청] ${hint.trim()}`;
  return { system: staffPrompt.trim() || FALLBACK_ANALYST, user };
}

const normalizeTrack = (s: string) => s.replace(/[\s·•・/,]/g, "");

/** "트랙 | 방향 | 이유" 줄을 읽는다. 목록에 없는 트랙은 버리고, 같은 트랙은 처음 것만, 최대 3개. */
export function parseAnalystPriorities(response: string, tracks: readonly TrackRecord[]): TrackPriority[] {
  const out: TrackPriority[] = [];
  for (const raw of response.split("\n")) {
    const line = raw.trim().replace(/^\*+|\*+$/g, "").trim().replace(LIST_MARK, "").trim().replace(/^\*+|\*+$/g, "").trim();
    if (!line || line.startsWith("트랙 |")) continue;
    const p = line.split("|").map((x) => x.trim().replace(/^[*"“”']+|[*"“”']+$/g, "").trim());
    if (p.length < 2 || !p[1]) continue;
    const name = normalizeTrack(p[0]);
    if (!name) continue;
    const track =
      tracks.find((t) => normalizeTrack(t.name) === name) ?? tracks.find((t) => t.key.toLowerCase() === p[0].toLowerCase());
    if (!track || out.some((x) => x.trackKey === track.key)) continue;
    out.push({ trackKey: track.key, trackName: track.name, direction: p[1], reason: p[2] ?? "" });
    if (out.length === PRIORITY_MAX) break;
  }
  return out;
}

// ── 번들에 들어가는 shared/staff 지시문 (맨 앞 HTML 주석은 빼고) ──
const staffFiles = import.meta.glob("@shared/staff/*.md", { query: "?raw", import: "default", eager: true }) as Record<string, string>;

export function loadStaffPrompt(name: "planner" | "editor" | "analyst"): string {
  const entry = Object.entries(staffFiles).find(([p]) => p.endsWith(`/${name}.md`));
  return entry ? cleanBasePrompt(entry[1]) : "";
}
