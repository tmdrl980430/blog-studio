import { load as yamlLoad } from "js-yaml";

export interface PostDraft {
  title: string;
  tags: string[];
  summary: string;
  body: string;
  topic: string;
  track: string;
  date: string; // yyyy-MM-dd
  thumbnail: string[];
}

const toTags = (v: unknown): string[] => {
  if (v == null) return [];
  if (Array.isArray(v)) return v.map((x) => String(x ?? "").trim().replace(/^#+/, "")).filter(Boolean);
  if (typeof v === "string") return v.split(",").map((t) => t.trim().replace(/^#+/, "")).filter(Boolean);
  return [String(v)];
};

/** AI 응답(YAML 머리말 + 마크다운) → 글. 데스크톱 PostParser와 같은 규칙. */
export function parsePost(response: string, topic: string, track: string, date: string): PostDraft {
  let text = response.replace(/\r\n/g, "\n").trim();
  const fence = /^```(?:markdown|md)?\s*\n([\s\S]*)\n```\s*$/.exec(text);
  const start = text.indexOf("---\n");
  if (fence) text = fence[1].trim();
  else if (start > 0) text = text.slice(start);

  const m = /^---\s*\n([\s\S]*?)\n---\s*\n([\s\S]*)$/.exec(text);
  if (!m) throw new Error("응답에서 머리말(---)을 찾지 못했어요. 응답 앞부분: " + text.slice(0, 200));
  let meta: Record<string, unknown>;
  try {
    meta = (yamlLoad(m[1]) ?? {}) as Record<string, unknown>;
  } catch (e) {
    throw new Error("머리말을 읽지 못했어요: " + (e as Error).message);
  }
  const s = (k: string) => (meta[k] == null ? "" : String(meta[k]).trim());
  return {
    title: s("title") || topic,
    tags: toTags(meta.tags),
    summary: s("summary"),
    body: m[2].trim(),
    topic,
    track,
    date,
    thumbnail: toTags(meta.thumbnail),
  };
}

export const tagLine = (p: PostDraft) => p.tags.map((t) => "#" + t).join(" ");

export function toFileText(p: PostDraft) {
  const esc = (x: string) => x.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
  return `---\ntitle: "${esc(p.title)}"\ntags: [${p.tags.join(", ")}]\nsummary: "${esc(p.summary)}"\ntopic: "${esc(p.topic)}"\ntrack: ${p.track}\ndate: ${p.date}\n---\n\n${p.body}\n`;
}
