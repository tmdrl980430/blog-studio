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

export const IDEA_COUNT = 5;
export const TOPIC_MAX = 60;
export const NOTE_MAX = 7;
export const PUBLISHABLE = "발행 가능";
export const NEEDS_EDIT = "수정 후 발행";
export const REWRITE = "다시 쓰기";

const FALLBACK_PLANNER =
  '당신은 한국어 블로그의 주제 기획자입니다. 검색될 만한 주제 5개를 "주제 | 핵심 검색 키워드 | 이유 | 카테고리" 형식으로 한 줄에 하나씩만 출력합니다. 카테고리는 [카테고리 목록]에서 그대로 하나 고릅니다.';
const FALLBACK_EDITOR =
  '당신은 한국어 블로그 글의 편집자입니다. 첫 줄에 "판정: 발행 가능 | 수정 후 발행 | 다시 쓰기" 중 하나, 다음 줄부터 "- 고칠 점"을 최대 7개 출력합니다.';

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

// ── 번들에 들어가는 shared/staff 지시문 (맨 앞 HTML 주석은 빼고) ──
const staffFiles = import.meta.glob("@shared/staff/*.md", { query: "?raw", import: "default", eager: true }) as Record<string, string>;

export function loadStaffPrompt(name: "planner" | "editor"): string {
  const entry = Object.entries(staffFiles).find(([p]) => p.endsWith(`/${name}.md`));
  return entry ? cleanBasePrompt(entry[1]) : "";
}
