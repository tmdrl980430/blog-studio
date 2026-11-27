// 발행 전 점검 — 데스크톱 PostChecker(src/BlogStudio.Core/Review/PostChecker.cs)와 같은 기준.
// 검색에 잘 걸리고 실수 없이 올라가도록 기계적으로 볼 수 있는 것만 본다.

export const TITLE_MIN = 12;
export const TITLE_MAX = 40;
export const TAGS_MIN = 5;
export const TAGS_MAX = 10;
export const BODY_MIN = 1500;

export interface CheckItem {
  ok: boolean;
  text: string;
}

interface Checkable {
  title: string;
  tags: string[];
  body: string;
}

const NEEDS_CHECK = /\(확인 필요\)/g;
const LEFTOVERS = /^[ \t]*(\[사진\s*\d+\s*[:：]|```svg)/gm;
const H2 = /^## /gm;
const TABLE = /^\|.+\|[ \t]*\r?\n\|[ \t:|-]+\|/m;
const IMAGE = /!\[[^\]]*\]\([^)]+\)/g;
const MARKUP = /```[\s\S]*?```|!\[[^\]]*\]\([^)]+\)|\[([^\]]*)\]\([^)]+\)|[#>*_`|-]/g;

const count = (re: RegExp, s: string) => (s.match(re) ?? []).length;
const n = (v: number) => v.toLocaleString("ko-KR");

/** 코드 블록·이미지·마크다운 기호를 뺀 글자 수 (공백 제외). */
export function textLength(markdown: string): number {
  const plain = markdown.replace(MARKUP, (_m, link?: string) => link ?? "");
  return [...plain].filter((c) => !/\s/.test(c)).length;
}

/**
 * 점검 목록. `body`는 그림(```svg)을 뽑아낸 뒤의 본문을 넘기고,
 * `extraImages`에는 앱이 따로 만드는 이미지(썸네일·그림) 수를 넘긴다.
 */
export function checkPost(post: Checkable, extraImages = 0): CheckItem[] {
  const list: CheckItem[] = [];
  const add = (ok: boolean, good: string, bad: string) => list.push({ ok, text: ok ? good : bad });

  const t = post.title.trim().length;
  add(t >= TITLE_MIN && t <= TITLE_MAX, `제목 ${t}자`,
    t > TITLE_MAX ? `제목 ${t}자 — 검색 결과에서 잘려요. ${TITLE_MAX}자 이하로` : `제목 ${t}자 — 너무 짧아요. 검색어가 들어가게 ${TITLE_MIN}자 이상`);

  const tags = post.tags.length;
  add(tags >= TAGS_MIN && tags <= TAGS_MAX, `태그 ${tags}개`,
    tags < TAGS_MIN ? `태그 ${tags}개 — ${TAGS_MIN}개 이상이면 검색 노출에 유리해요` : `태그 ${tags}개 — ${TAGS_MAX}개 이하로 추리기`);

  const len = textLength(post.body);
  add(len >= BODY_MIN, `본문 ${n(len)}자`, `본문 ${n(len)}자 — ${n(BODY_MIN)}자 이상이면 좋아요`);

  const h2 = count(H2, post.body);
  add(h2 >= 3, `소제목 ${h2}개`, `소제목 ${h2}개 — 3개 이상으로 나누면 읽기 쉬워요`);

  const images = count(IMAGE, post.body) + extraImages;
  add(images > 0, `이미지 ${images}장`, "이미지 없음 — 대표 이미지(썸네일)가 있으면 클릭이 늘어요");

  const unknown = count(NEEDS_CHECK, post.body) + count(NEEDS_CHECK, post.title);
  add(unknown === 0, "확인할 정보 없음", `'(확인 필요)' ${unknown}곳 — 발행 전에 채우거나 지우기`);

  const leftovers = count(LEFTOVERS, post.body);
  if (leftovers > 0) list.push({ ok: false, text: `처리 안 된 사진·그림 자리 ${leftovers}곳 — 본문에서 지우기` });

  if (TABLE.test(post.body)) list.push({ ok: true, text: "표 있음" });
  return list;
}
