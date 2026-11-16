import type { ThemePack, Track } from "./themes";

export interface PromptPair {
  system: string;
  user: string;
}

export interface HistoryLine {
  date: string;
  track: string;
  title: string;
}

export interface PostInputs {
  name: string;
  memo: string;
}

export const HISTORY_LIMIT = 15;
export const STYLE_SAMPLE_LIMIT = 1500;
export const AUTO_TOPIC =
  "(커리큘럼의 주제를 모두 다뤘습니다. 이전 글 목록을 보고 다음으로 공부하기 좋은 심화 주제를 직접 하나 골라 작성하세요.)";

/** 데스크톱 PromptBuilder.Build 와 같은 내용 — 고정 부분(system)을 앞에 둬서 프롬프트 캐시가 잘 맞게. */
export function buildPrompt(
  base: string,
  theme: ThemePack,
  track: Track,
  topic: string | null,
  history: HistoryLine[],
  author: string,
  opts: { extra?: string; inputs?: PostInputs; styleSample?: string } = {},
): PromptPair {
  let system = base
    .replaceAll("{author}", author.trim() || "블로그 주인")
    .replaceAll("{tone}", theme.tone)
    .replaceAll("{length}", theme.length);
  system += `\n\n[테마] ${theme.name}`;
  if (theme.description) system += ` — ${theme.description}`;
  if (theme.structure.length) system += "\n[글 구조]" + theme.structure.map((l) => "\n- " + l).join("");
  if (theme.rules.length) system += "\n[테마 규칙]" + theme.rules.map((l) => "\n- " + l).join("");
  const style = opts.styleSample?.trim();
  if (style)
    system += "\n\n[내 문체 예시 — 말투·문장 길이·어미·자주 쓰는 표현만 따라 하고, 내용·사실은 가져오지 않음]\n" + style.slice(0, STYLE_SAMPLE_LIMIT);

  const recent = history
    .filter((h) => h.track === track.key)
    .sort((a, b) => b.date.localeCompare(a.date))
    .slice(0, HISTORY_LIMIT)
    .map((h) => `- ${h.date} ${h.title}`);

  const memo = opts.inputs?.memo.trim();
  const name = opts.inputs?.name.trim();
  let user =
    `[트랙] ${track.name}\n[트랙 작성 가이드] ${track.guide}` +
    `\n\n[오늘의 주제] ${memo ? `아래 [내 메모]의 경험을 바탕으로 한 후기${name ? ` — ${name}` : ""}` : topic?.trim() || AUTO_TOPIC}` +
    `\n\n[이 트랙에서 이전에 쓴 글 (최근 순)]\n${recent.length ? recent.join("\n") : "(없음 — 이 트랙의 첫 글)"}` +
    `\n\n이전 글과 내용이 겹치지 않게 하고, 자연스럽게 이어지면 본문에서 "지난 글에서 다룬 ~"처럼 한 번 언급해도 좋습니다.`;
  if (memo) user += `\n\n[내 메모 — 이 내용만 사실로 사용]\n${memo}`;
  if (opts.extra?.trim()) user += `\n[추가 요청] ${opts.extra.trim()}`;
  return { system, user };
}

export function buildRevision(fileText: string, instruction: string, author: string): PromptPair {
  return {
    system: `당신은 한국어 블로그 글의 편집자입니다. 블로그 주인 ${author.trim() || "글쓴이"}의 글을 [고칠 점]대로 고칩니다.
- 출력은 고친 글 전체 하나만: 맨 앞 front matter(--- ~ ---)와 본문. 설명·인사말·코드 펜스로 감싸기 금지.
- front matter의 topic, track, date 줄은 그대로 둡니다. title·tags·summary는 [고칠 점]이 요구할 때만 바꿉니다.
- [고칠 점]과 관계없는 문장은 바꾸지 않습니다.
- 사실을 새로 지어내지 않습니다. 모르는 정보는 "(확인 필요)"로 남깁니다.
- 표는 마크다운 표, 코드는 언어 태그가 있는 펜스 코드 블록을 씁니다.`,
    user: `[고칠 점]\n${instruction.trim()}\n\n[지금 글]\n${fileText}`,
  };
}
