import { describe, expect, it, vi } from "vitest";
import { loadThemes, loadBasePrompt, colorFor } from "../lib/themes";
import { parsePost, toFileText } from "../lib/posts";
import { buildPrompt, buildRevision, AUTO_TOPIC } from "../lib/prompt";
import { plan, planTrack, dayKey } from "../lib/planner";
import { toBlogHtml, extractFigures } from "../lib/render";
import { addPost, load, save, updatePost, exportJson } from "../lib/storage";
import { generate, ClaudeError } from "../lib/claude";
import { splitTitle } from "../lib/images";

const themes = loadThemes();
const base = loadBasePrompt();

const RESPONSE = `잡담입니다\n---\ntitle: "BFS로 미로 최단 거리"\ntags: [BFS, "#그래프", 파이썬]\nsummary: 요약\nthumbnail: [BFS로, 최단 거리]\n---\n\n## 오늘 공부할 내용\n- [ ] 큐 이해\n\n본문 \`deque\` 사용\n\n| 방법 | 시간 |\n|---|---|\n| BFS | O(V+E) |\n\n\`\`\`svg\n<!-- caption: 탐색 순서 -->\n<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 720 200"><rect width="720" height="200" fill="#fff"/></svg>\n\`\`\`\n\n\`\`\`python\nprint(1)\n\`\`\`\n\n<script>alert(1)</script>\n<img src=x onerror="alert(1)">\n`;

describe("shared 파일", () => {
  it("데스크톱과 같은 테마·커리큘럼·프롬프트를 번들에 담는다", () => {
    expect(Object.keys(themes).sort()).toEqual(["life", "tech"]);
    expect(themes.tech.tracks).toEqual(["algorithm", "cs", "csharp_windows"]);
    expect(themes.tech.curriculum.cs.topics.length).toBeGreaterThanOrEqual(30);
    expect(themes.life.curriculum.food.memoHint).toContain("가게 이름");
    expect(colorFor(themes.life, "food")).toBe("#C2410C");
    expect(base.startsWith("당신은")).toBe(true);
  });
});

describe("parsePost", () => {
  it("앞 잡담을 건너뛰고 머리말·태그·본문을 읽는다", () => {
    const p = parsePost(RESPONSE, "BFS", "algorithm", "2026-11-16");
    expect(p.title).toBe("BFS로 미로 최단 거리");
    expect(p.tags).toEqual(["BFS", "그래프", "파이썬"]);
    expect(p.thumbnail).toEqual(["BFS로", "최단 거리"]);
    expect(p.body.startsWith("## 오늘 공부할 내용")).toBe(true);
    expect(parsePost(toFileText(p), "BFS", "algorithm", "2026-11-16")).toEqual({ ...p, thumbnail: [] });
  });
  it("머리말이 없으면 알려 준다", () => {
    expect(() => parsePost("그냥 글", "t", "a", "2026-11-16")).toThrow(/머리말/);
  });
});

describe("buildPrompt", () => {
  const tech = themes.tech;
  it("최근 이전 글, 문체, 추가 요청을 넣는다", () => {
    const p = buildPrompt(base, tech, tech.curriculum.cs, "TCP", [
      { date: "2026-11-01", track: "cs", title: "OSI 7계층" },
      { date: "2026-11-02", track: "algorithm", title: "다른 트랙" },
    ], "김승기", { extra: "예시 많이", styleSample: "안녕하세요~" });
    expect(p.system).toContain("김승기");
    expect(p.system).toContain("[내 문체 예시");
    expect(p.user).toContain("[오늘의 주제] TCP");
    expect(p.user).toContain("2026-11-01 OSI 7계층");
    expect(p.user).not.toContain("다른 트랙");
    expect(p.user).toContain("[추가 요청] 예시 많이");
  });
  it("메모가 있으면 후기, 주제가 없으면 자동 주제", () => {
    const life = themes.life;
    const memo = buildPrompt(base, life, life.curriculum.food, "아이디어", [], "", { inputs: { name: "라멘집", memo: "돈코츠 11,000원" } });
    expect(memo.user).toContain("후기 — 라멘집");
    expect(memo.user).toContain("[내 메모 — 이 내용만 사실로 사용]\n돈코츠 11,000원");
    expect(buildPrompt(base, tech, tech.curriculum.cs, null, [], "").user).toContain(AUTO_TOPIC);
  });
  it("고치기 프롬프트는 지금 글 전체를 담는다", () => {
    const r = buildRevision("---\ntitle: x\n---\n본문", " 짧게 ", "");
    expect(r.user).toBe("[고칠 점]\n짧게\n\n[지금 글]\n---\ntitle: x\n---\n본문");
  });
});

describe("planner", () => {
  it("요일별 트랙과 진도로 주제를 정한다", () => {
    const tech = themes.tech;
    const wed = "2026-11-18";
    expect(dayKey(wed)).toBe("wed");
    const p = plan(tech, {}, wed, { [tech.defaultSchedule.wed]: 2 })!;
    expect(p.track.key).toBe(tech.defaultSchedule.wed);
    expect(p.topic).toBe(p.track.topics[2]);
    expect(plan(tech, { wed: "rest" }, wed, {})).toBeNull();
    expect(planTrack(themes.life.curriculum.food, wed, { food: 999 }).topic).toBeNull();
  });
});

describe("render", () => {
  it("그림을 꺼내고, 소독하고, 인라인 스타일을 넣는다", () => {
    const post = parsePost(RESPONSE, "BFS", "algorithm", "2026-11-16");
    const { markdown, figures } = extractFigures(post.body);
    expect(figures).toEqual([expect.objectContaining({ number: 2, caption: "탐색 순서" })]);

    const copy = toBlogHtml(markdown, "copy", figures, "");
    expect(copy).toContain("[이미지 1: 썸네일]");
    expect(copy).toContain("[이미지 2: 탐색 순서]");
    expect(copy).toContain('<h2 style="font-size:22px');
    expect(copy).toContain("<table style=");
    expect(copy).toContain("☐ 큐 이해");
    expect(copy).toMatch(/<code style="background:#f1f1f1/);
    expect(copy).toMatch(/<pre style="background:#f8f8f8/);
    expect(copy).not.toContain("<script");
    expect(copy).not.toContain("onerror");

    const preview = toBlogHtml(markdown, "preview", figures, "data:image/png;base64,AA");
    expect(preview).toContain('src="data:image/svg+xml');
    expect(preview).toContain("그림. 탐색 순서");
    expect(preview.startsWith('<img alt="썸네일" src="data:image/png;base64,AA"')).toBe(true);
  });
  it("썸네일 제목을 두 줄로", () => {
    expect(splitTitle("BFS로 미로 최단 거리 구하기")).toEqual(["BFS로 미로 최단", "거리 구하기"]);
    expect(splitTitle("짧은 제목")).toEqual(["짧은 제목"]);
  });
});

describe("storage", () => {
  it("글 저장·진도·올림 표시·내보내기", () => {
    const mem = new Map<string, string>();
    const store = { getItem: (k: string) => mem.get(k) ?? null, setItem: (k: string, v: string) => void mem.set(k, v) } as Storage;
    let d = load(store);
    d = { ...d, settings: { ...d.settings, apiKey: "sk-secret" } };
    const post = parsePost(RESPONSE, "BFS", "algorithm", "2026-11-16");
    d = addPost(d, post, true);
    d = addPost(d, { ...post, track: "food" }, false);
    expect(d.progress).toEqual({ algorithm: 1 });
    d = updatePost(d, d.posts[0].id, { publishedTo: ["naver"] });
    save(d, store);
    const again = load(store);
    expect(again.posts[0].publishedTo).toEqual(["naver"]);
    expect(exportJson(again)).not.toContain("sk-secret");
  });
});

describe("Claude API", () => {
  const prompt = { system: "S", user: "U" };
  it("내 키로 호출하고 system에 캐시를 건다", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ model: "claude-sonnet-5-5", content: [{ type: "text", text: "글" }], usage: { input_tokens: 10, output_tokens: 5 } })));
    const r = await generate(" sk-ant ", "sonnet", prompt, { fetchImpl });
    expect(r).toEqual({ text: "글", model: "claude-sonnet-5-5", inputTokens: 10, outputTokens: 5 });
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("https://api.anthropic.com/v1/messages");
    const headers = init.headers as Record<string, string>;
    expect(headers["x-api-key"]).toBe("sk-ant");
    expect(headers["anthropic-dangerous-direct-browser-access"]).toBe("true");
    const body = JSON.parse(init.body as string);
    expect(body.model).toBe("claude-sonnet-5-5");
    expect(body.system[0].cache_control).toEqual({ type: "ephemeral" });
  });
  it("키가 없거나 틀리면 알기 쉬운 메시지", async () => {
    await expect(generate("", "sonnet", prompt)).rejects.toThrow(ClaudeError);
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ error: { message: "invalid x-api-key" } }), { status: 401 }));
    await expect(generate("k", "sonnet", prompt, { fetchImpl })).rejects.toThrow("API 키가 맞지 않아요");
  });
});
