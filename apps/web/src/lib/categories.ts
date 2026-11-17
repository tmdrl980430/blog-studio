/**
 * 블로그 카테고리 ("상위 > 하위", 이슈 #19). 데스크톱 BlogCategory·BlogCategories와 같은 규칙.
 * 웹앱에는 아직 카테고리 목록 설정이 없어 기본 체계만 쓴다.
 */
export interface BlogCategory {
  parent: string;
  child: string;
}

export const SEPARATOR = " > ";

export const formatCategory = (c: BlogCategory) => (c.child ? c.parent + SEPARATOR + c.child : c.parent);

/** "개발 > CS 기초", "개발>CS 기초", "개발 / CS 기초"를 읽는다. 빈 값이면 null. */
export function parseCategory(text: string | null | undefined): BlogCategory | null {
  if (text == null || !text.trim()) return null;
  const i = text.search(/[>/]/);
  const strip = (s: string) => s.trim().replace(/^["']+|["']+$/g, "").trim();
  const parent = strip(i < 0 ? text : text.slice(0, i));
  const child = i < 0 ? "" : strip(text.slice(i + 1));
  if (!parent) return child ? { parent: child, child: "" } : null;
  return { parent, child };
}

export const DEFAULT_CATEGORIES: readonly BlogCategory[] = [
  { parent: "개발", child: "알고리즘·코테" },
  { parent: "개발", child: "CS 기초" },
  { parent: "개발", child: "C#·Windows" },
  { parent: "생활", child: "맛집·카페" },
  { parent: "생활", child: "여행·일상" },
  { parent: "생활", child: "패션·코디" },
  { parent: "생활", child: "제품 리뷰" },
];

const TRACK_DEFAULTS: Record<string, number> = {
  algorithm: 0,
  cs: 1,
  csharp_windows: 2,
  food: 3,
  travel: 4,
  fashion: 5,
  product: 6,
};

const key = (s: string) => s.replace(/\s/g, "");

/** 트랙의 기본 카테고리. 모르는 트랙이면 null (블로그 기본 카테고리로 발행). */
export function categoryForTrack(track: string, list: readonly BlogCategory[] = DEFAULT_CATEGORIES): BlogCategory | null {
  const i = TRACK_DEFAULTS[track];
  if (i === undefined) return null;
  return list === DEFAULT_CATEGORIES ? DEFAULT_CATEGORIES[i] : matchCategory(formatCategory(DEFAULT_CATEGORIES[i]), list);
}

/** 목록 안의 카테고리로 맞춘다. 공백·구분자 차이는 무시하고, 하위 이름만 맞아도 찾는다. 없으면 null — 새로 만들지 않는다. */
export function matchCategory(text: string | null | undefined, list: readonly BlogCategory[] = DEFAULT_CATEGORIES): BlogCategory | null {
  const wanted = parseCategory(text);
  if (!wanted) return null;
  const exact = list.find((c) => key(c.parent) === key(wanted.parent) && key(c.child) === key(wanted.child));
  if (exact) return exact;
  if (!wanted.child) {
    const byChild = list.filter((c) => key(c.child) === key(wanted.parent));
    if (byChild.length === 1) return byChild[0];
  }
  return null;
}

/** 글에 저장할 카테고리: 적힌 값이 목록에 있으면 그것, 아니면 트랙 기본값, 둘 다 없으면 "". */
export function resolveCategory(written: string | null | undefined, track: string, list: readonly BlogCategory[] = DEFAULT_CATEGORIES): string {
  const c = matchCategory(written, list) ?? categoryForTrack(track, list);
  return c ? formatCategory(c) : "";
}
