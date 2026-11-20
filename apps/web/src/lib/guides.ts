import type { ThemePack } from "./themes";

/**
 * "블로그 준비" 가이드 (shared/guides/*.md). 데스크톱 BlogGuides와 같은 규칙:
 * 머리말(---)과 HTML 주석을 떼고, {theme.*} 자리에 테마 design 값을 채운다.
 * 가이드 파일은 데스크톱·웹이 같이 쓰므로 읽기만 한다.
 */
export interface GuideCodeBlock {
  language: string; // "css" | "html"
  code: string;
  /** 복사 버튼 옆 이름: "CSS · /* 블로그 스튜디오 글 서식 *\/" */
  label: string;
}

export interface BlogGuide {
  key: string;
  title: string;
  markdown: string;
  codeBlocks: GuideCodeBlock[];
}

const FRONT_MATTER = /^---\s*\n([\s\S]*?)\n---\s*\n/;
const COMMENT = /<!--[\s\S]*?-->\s*/g;
const PLACEHOLDER = /\{theme\.([a-z_]+)\}/g;
const COPY_BLOCK = /^```(css|html)[ \t]*\n([\s\S]*?)\n```[ \t]*$/gm;
export const DEFAULT_GUIDES = ["naver", "tistory", "design"];

function valueFor(key: string, theme: ThemePack): string | null {
  const d = theme.design;
  const value =
    key === "name" ? theme.name
    : key === "accent" ? d.accent
    : key === "layout" ? d.layout
    : key === "categories" ? d.categories.join(" / ")
    : key === "tistory_skin" ? d.tistorySkin
    : null;
  return value && value.trim() ? value : null;
}

/** {theme.categories} 같은 자리를 채운다. 값이 없거나 모르는 이름이면 그대로 둔다. */
export const fillGuide = (text: string, theme: ThemePack) =>
  text.replace(PLACEHOLDER, (m, key: string) => valueFor(key, theme) ?? m);

export function codeLabel(language: string, code: string): string {
  let first = code.split("\n").map((l) => l.trim()).find((l) => l.length > 0) ?? "";
  if (first.length > 48) first = first.slice(0, 48) + "…";
  return `${language.toUpperCase()} · ${first}`;
}

export function parseGuide(key: string, text: string, theme: ThemePack): BlogGuide {
  text = text.replace(/\r\n/g, "\n");
  let title = key;
  const front = FRONT_MATTER.exec(text);
  if (front) {
    const line = front[1].split("\n").find((l) => l.startsWith("title:"));
    if (line) title = line.slice("title:".length).trim().replace(/^"+|"+$/g, "");
    text = text.slice(front[0].length);
  }
  const markdown = fillGuide(text.replace(COMMENT, ""), theme).trim();
  const codeBlocks = [...markdown.matchAll(COPY_BLOCK)].map((m) => ({
    language: m[1],
    code: m[2],
    label: codeLabel(m[1], m[2]),
  }));
  return { key, title, markdown, codeBlocks };
}

/** 테마의 design.guides 순서대로 (없으면 기본 순서). 없는 파일은 건너뛴다. */
export function loadGuides(theme: ThemePack, files: Record<string, string> = guideFiles()): BlogGuide[] {
  const keys = theme.design.guides.length > 0 ? theme.design.guides : DEFAULT_GUIDES;
  return keys.filter((k) => k in files).map((k) => parseGuide(k, files[k], theme));
}

// ── 번들에 들어가는 shared/guides ──
const raw = import.meta.glob("@shared/guides/*.md", { query: "?raw", import: "default", eager: true }) as Record<string, string>;
function guideFiles(): Record<string, string> {
  return Object.fromEntries(Object.entries(raw).map(([p, t]) => [p.split("/").at(-1)!.replace(/\.md$/, ""), t]));
}
