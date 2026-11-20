import { load as yamlLoad } from "js-yaml";

/** shared/themes 의 테마·트랙 (데스크톱 앱과 같은 파일). */
export interface Track {
  key: string;
  name: string;
  guide: string;
  memoHint: string;
  topics: string[];
}

export interface ThemePack {
  slug: string;
  name: string;
  description: string;
  tone: string;
  length: string;
  modelTier: string;
  structure: string[];
  rules: string[];
  tracks: string[];
  defaultSchedule: Record<string, string>;
  trackColors: Record<string, string>;
  accent: string;
  /** design 항목 중 블로그 준비 가이드({theme.*})에 쓰는 값 */
  design: ThemeDesign;
  curriculum: Record<string, Track>;
}

export interface ThemeDesign {
  /** theme.yaml 원래 값 (없으면 "" — 가이드 자리는 그대로 둠) */
  accent: string;
  layout: string;
  categories: string[];
  tistorySkin: string;
  /** 보여 줄 가이드 순서 (shared/guides/<key>.md) */
  guides: string[];
}

type Raw = Record<string, unknown>;
const str = (v: unknown) => (v == null ? "" : String(v)).trim();
const list = (v: unknown) => (Array.isArray(v) ? v.map(str).filter(Boolean) : []);
const dict = (v: unknown) =>
  v && typeof v === "object" && !Array.isArray(v)
    ? Object.fromEntries(Object.entries(v as Raw).map(([k, x]) => [k, str(x)]))
    : {};

/** theme.yaml + curriculum/*.yaml 텍스트로 테마 만들기 (테스트에서도 그대로 씀). */
export function parseTheme(themeYaml: string, curriculumFiles: Record<string, string>): ThemePack {
  const t = (yamlLoad(themeYaml) ?? {}) as Raw;
  const design = (t.design ?? {}) as Raw;
  const curriculum: Record<string, Track> = {};
  for (const [key, text] of Object.entries(curriculumFiles)) {
    const c = (yamlLoad(text) ?? {}) as Raw;
    curriculum[key] = {
      key,
      name: str(c.name) || key,
      guide: str(c.guide),
      memoHint: str(c.memo_hint),
      topics: list(c.topics),
    };
  }
  return {
    slug: str(t.slug),
    name: str(t.name),
    description: str(t.description),
    tone: str(t.tone),
    length: str(t.length),
    modelTier: str(t.model_tier) || "main",
    structure: list(t.structure),
    rules: list(t.rules),
    tracks: list(t.tracks),
    defaultSchedule: dict(t.default_schedule),
    trackColors: dict(design.track_colors),
    accent: str(design.accent) || "#2F5BEA",
    design: {
      accent: str(design.accent),
      layout: str(design.layout),
      categories: list(design.categories),
      tistorySkin: str(design.tistory_skin),
      guides: list(design.guides),
    },
    curriculum,
  };
}

export const colorFor = (theme: ThemePack, track: string) =>
  theme.trackColors[track] || theme.accent || "#2F5BEA";

/** 공통 프롬프트: 맨 앞 HTML 주석은 사람용 메모라 뺀다. */
export const cleanBasePrompt = (text: string) => text.replace(/^\s*<!--[\s\S]*?-->\s*/, "").trim();

// ── 번들에 들어가는 shared 파일들 ──
const themeFiles = import.meta.glob("@shared/themes/*/theme.yaml", { query: "?raw", import: "default", eager: true }) as Record<string, string>;
const curriculumFiles = import.meta.glob("@shared/themes/*/curriculum/*.yaml", { query: "?raw", import: "default", eager: true }) as Record<string, string>;
const prompts = import.meta.glob("@shared/prompts/base.md", { query: "?raw", import: "default", eager: true }) as Record<string, string>;

export function loadThemes(): Record<string, ThemePack> {
  const out: Record<string, ThemePack> = {};
  for (const [path, text] of Object.entries(themeFiles)) {
    const slug = path.split("/").at(-2)!;
    const tracks = Object.fromEntries(
      Object.entries(curriculumFiles)
        .filter(([p]) => p.split("/").at(-3) === slug)
        .map(([p, t]) => [p.split("/").at(-1)!.replace(/\.yaml$/, ""), t]),
    );
    out[slug] = parseTheme(text, tracks);
  }
  return out;
}

export const loadBasePrompt = () => cleanBasePrompt(Object.values(prompts)[0] ?? "");
