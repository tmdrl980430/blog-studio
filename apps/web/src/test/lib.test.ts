import { describe, expect, it, vi } from "vitest";
import { loadThemes, loadBasePrompt, colorFor, makesThumbnail } from "../lib/themes";
import { parsePost, toFileText } from "../lib/posts";
import { parseCategory, matchCategory, resolveCategory, categoryForTrack, formatCategory } from "../lib/categories";
import { buildPrompt, buildRevision, AUTO_TOPIC } from "../lib/prompt";
import { plan, planTrack, dayKey } from "../lib/planner";
import { toBlogHtml, extractFigures } from "../lib/render";
import { addPost, load, save, updatePost, exportJson } from "../lib/storage";
import { generate, ClaudeError } from "../lib/claude";
import { splitTitle } from "../lib/images";
import { loadGuides, parseGuide, fillGuide } from "../lib/guides";
import {
  buildTopicIdeas, parseTopicIdeas, buildEditorReview, parseEditorReview, revisionFromReview, keywordRequest, loadStaffPrompt,
  blogStats, trackRecords, buildAnalystPrompt, parseAnalystPriorities,
} from "../lib/staff";

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
  it("카테고리 없는 예전 글은 category 줄 없이 그대로 쓴다", () => {
    const p = parsePost(RESPONSE, "BFS", "algorithm", "2026-11-16");
    expect(p.category).toBe("");
    expect(toFileText(p)).not.toContain("category:");
    const { category: _drop, ...old } = p; // 예전 브라우저 데이터 (category 필드 없음)
    expect(toFileText(old)).toBe(toFileText(p));
  });
  it("category를 읽고 데스크톱과 같은 자리에 쓴다", () => {
    const text = `---\ntitle: "제목"\ntags: [a]\nsummary: "요약"\ntopic: "주제"\ntrack: cs\ncategory: "개발>CS 기초"\ndate: 2026-11-17\n---\n\n본문\n`;
    const p = parsePost(text, "주제", "cs", "2026-11-17");
    expect(p.category).toBe("개발 > CS 기초");
    expect(toFileText(p)).toContain('track: cs\ncategory: "개발 > CS 기초"\ndate: 2026-11-17\n');
    expect(parsePost(toFileText(p), "주제", "cs", "2026-11-17")).toEqual({ ...p, thumbnail: [] });
  });
});

describe("categories", () => {
  it("구분자·따옴표·공백 차이를 맞춰 읽는다", () => {
    expect(parseCategory("개발 / CS 기초")).toEqual({ parent: "개발", child: "CS 기초" });
    expect(parseCategory(" '생활' ")).toEqual({ parent: "생활", child: "" });
    expect(parseCategory("> CS 기초")).toEqual({ parent: "CS 기초", child: "" });
    expect(parseCategory("  ")).toBeNull();
    expect(formatCategory({ parent: "개발", child: "C#·Windows" })).toBe("개발 > C#·Windows");
  });
  it("목록 안에서만 찾고 없으면 새로 만들지 않는다", () => {
    expect(formatCategory(matchCategory("개발>CS기초")!)).toBe("개발 > CS 기초");
    expect(formatCategory(matchCategory("맛집·카페")!)).toBe("생활 > 맛집·카페");
    expect(matchCategory("개발 > 게임")).toBeNull();
    expect(matchCategory("")).toBeNull();
  });
  it("목록 밖이면 트랙 기본값, 모르는 트랙이면 빈 값", () => {
    expect(resolveCategory("생활 > 여행·일상", "cs")).toBe("생활 > 여행·일상");
    expect(resolveCategory("개발 > 게임", "algorithm")).toBe("개발 > 알고리즘·코테");
    expect(resolveCategory("", "product")).toBe("생활 > 제품 리뷰");
    expect(resolveCategory("", "unknown")).toBe("");
    expect(categoryForTrack("fashion", [{ parent: "생활", child: "맛집·카페" }])).toBeNull();
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
  it("design.thumbnail 이 typo_card(또는 비어 있음)일 때만 썸네일을 만든다", () => {
    expect(themes.tech.design.thumbnail).toBe("typo_card");
    expect(makesThumbnail(themes.tech.design)).toBe(true);
    if (themes.life) expect(makesThumbnail(themes.life.design)).toBe(true);
    expect(makesThumbnail({ thumbnail: "" })).toBe(true);
    expect(makesThumbnail({ thumbnail: " Typo_Card " })).toBe(true);
    expect(makesThumbnail({ thumbnail: "none" })).toBe(false);
    expect(makesThumbnail({ thumbnail: "photo" })).toBe(false);
  });
  it("썸네일을 만들지 않으면 자리 표시 없이 그림이 1번부터", () => {
    const post = parsePost(RESPONSE, "BFS", "algorithm", "2026-11-16");
    const { markdown, figures } = extractFigures(post.body, 1);
    expect(figures).toEqual([expect.objectContaining({ number: 1, caption: "탐색 순서" })]);
    const copy = toBlogHtml(markdown, "copy", figures);
    expect(copy).not.toContain("썸네일");
    expect(copy).toContain("[이미지 1: 탐색 순서]");
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

describe("AI 직원 (shared/staff)", () => {
  it("지시문을 번들에서 읽고 맨 앞 주석은 뺀다", () => {
    expect(loadStaffPrompt("planner").startsWith("당신은")).toBe(true);
    expect(loadStaffPrompt("planner")).toContain("주제 | 핵심 검색 키워드 | 이 주제를 고른 이유 (20자 안팎) | 카테고리");
    expect(loadStaffPrompt("editor")).toContain("{author}");
  });
  it("기획자 프롬프트: 같은 트랙 이전 글, 카테고리 목록, 요청", () => {
    const track = themes.tech.curriculum.cs;
    const p = buildTopicIdeas("", themes.tech, track, [
      { date: "2026-11-01", track: "cs", title: "프로세스와 스레드" },
      { date: "2026-11-02", track: "algorithm", title: "BFS" },
    ], "면접 대비");
    expect(p.system).toContain("주제 기획자");
    expect(p.user).toContain("- 프로세스와 스레드");
    expect(p.user).not.toContain("- BFS");
    expect(p.user).toContain("- 개발 > CS 기초");
    expect(p.user).toContain("[이 트랙 기본 카테고리] 개발 > CS 기초");
    expect(p.user).toContain("[요청] 면접 대비");
  });
  it("기획자 응답: 번호·따옴표·머리줄·중복·긴 주제 정리, 카테고리는 목록 안만", () => {
    const ideas = parseTopicIdeas([
      "주제 | 핵심 검색 키워드 | 이유 | 카테고리",
      '1. "TCP 3-way handshake" | TCP 연결 | 면접 단골 | 개발>CS 기초',
      "- TCP 3-way handshake | 중복 | x | 개발 > CS 기초",
      "가".repeat(61) + " | 길다",
      "캐시 지역성 | 캐시 | 다음 단계 | 개발 > 게임",
      "힙 정렬",
    ].join("\n"));
    expect(ideas.map((i) => i.topic)).toEqual(["TCP 3-way handshake", "캐시 지역성", "힙 정렬"]);
    expect(ideas[0]).toEqual({ topic: "TCP 3-way handshake", keyword: "TCP 연결", reason: "면접 단골", category: "개발 > CS 기초" });
    expect(ideas[1].category).toBe("");
    expect(ideas[2]).toEqual({ topic: "힙 정렬", keyword: "", reason: "", category: "" });
    expect(parseTopicIdeas(Array.from({ length: 8 }, (_, i) => `주제${i}`).join("\n"))).toHaveLength(5);
  });
  it("편집자 응답: 판정 읽기, 형식 안내 줄은 판정 아님, 의견 있으면 발행 가능 → 수정 후 발행", () => {
    expect(parseEditorReview("**판정: 발행 가능**")).toEqual({ verdict: "발행 가능", notes: [] });
    expect(parseEditorReview("판정: 다시 쓰기\n- 사실 오류")).toEqual({ verdict: "다시 쓰기", notes: ["사실 오류"] });
    expect(parseEditorReview("판정: 발행 가능 | 수정 후 발행 | 다시 쓰기\n- 고칠 점").verdict).toBe("수정 후 발행");
    expect(parseEditorReview("판정: 발행 가능\n1. 제목").verdict).toBe("수정 후 발행");
    expect(parseEditorReview("판정: 수정 후 발행\n" + Array.from({ length: 9 }, (_, i) => `- 의견${i}`).join("\n")).notes).toHaveLength(7);
    expect(parseEditorReview("아무 말")).toEqual({ verdict: "발행 가능", notes: [] });
  });
  describe("성과 분석가", () => {
    const tracks = [{ key: "algorithm", name: "알고리즘" }, { key: "food", name: "맛집·카페" }, { key: "cs", name: "CS 기초" }];
    const posts = [
      { date: "2026-11-18", track: "algorithm", publishedTo: ["naver"] },
      { date: "2026-11-17", track: "algorithm", publishedTo: ["naver", "tistory"] },
      { date: "2026-11-16", track: "food", publishedTo: [] },
      { date: "2026-11-01", track: "food" },
      { date: "2026-10-01", track: "지운트랙", publishedTo: ["tistory"] },
    ];
    it("통계: 주(월요일 시작)·달·30일·연속·올림", () => {
      expect(blogStats(posts, "2026-11-18")).toEqual({
        total: 5, thisMonth: 4, thisWeek: 3, last30Days: 4, streakDays: 3, bestStreak: 3,
        publishedNaver: 2, publishedTistory: 2, unpublished: 2,
      });
      expect(blogStats(posts, "2026-11-19").streakDays).toBe(3);  // 오늘 안 썼으면 어제부터
      expect(blogStats(posts, "2026-11-20").streakDays).toBe(0);
    });
    it("지시문을 읽고, 프롬프트에 트랙별 기록을 넣는다 (목록 밖 트랙은 세지 않음)", () => {
      expect(loadStaffPrompt("analyst")).toContain("트랙 | 다음 주제 방향 | 이유");
      const records = trackRecords(posts, [...tracks, tracks[0]]);
      expect(records.map((r) => r.posts)).toEqual([2, 2, 0]);
      const p = buildAnalystPrompt("", blogStats(posts, "2026-11-18"), records, "2026-11-18", " 수익 ");
      expect(p.system).toContain("성과 분석가");
      expect(p.user).toContain("[전체] 글 5편 · 이번 주 3편 · 이번 달 4편 · 최근 30일 4편 · 연속 3일 (최고 3일)");
      expect(p.user).toContain("[올림] 네이버 2편 · 티스토리 2편 · 아직 안 올린 글 2편");
      expect(p.user).toContain("- 알고리즘: 글 2편 · 올림 2편 · 마지막 글 오늘");
      expect(p.user).toContain("- 맛집·카페: 글 2편 · 올림 0편 · 마지막 글 2일 전");
      expect(p.user).toContain("- CS 기초: 아직 글 없음");
      expect(p.user).not.toContain("지운트랙");
      expect(p.user.endsWith("[요청] 수익")).toBe(true);
    });
    it("응답: 이름·키로 맞추고 목록 밖·중복·방향 없는 줄은 버리고 최대 3개", () => {
      const records = trackRecords(posts, [...tracks, { key: "travel", name: "여행·일상" }]);
      const picks = parseAnalystPriorities([
        "트랙 | 다음 주제 방향 | 이유",
        '1. **CS기초 | "캐시 지역성" | 글이 없음**',
        "- 없는 트랙 | 방향 | 이유",
        "맛집 카페 | | 방향 없음",
        "맛집 · 카페 | 성수 브런치",
        "CS 기초 | 중복 | x",
        "ALGORITHM | 다익스트라 | 꾸준함",
        "여행·일상 | 넘침 | x",
      ].join("\n"), records);
      expect(picks).toEqual([
        { trackKey: "cs", trackName: "CS 기초", direction: "캐시 지역성", reason: "글이 없음" },
        { trackKey: "food", trackName: "맛집·카페", direction: "성수 브런치", reason: "" },
        { trackKey: "algorithm", trackName: "알고리즘", direction: "다익스트라", reason: "꾸준함" },
      ]);
    });
  });
  it("편집자 프롬프트·고치기 요청·키워드 요청", () => {
    expect(buildEditorReview("주인 {author}", "글", "").system).toBe("주인 글쓴이");
    expect(buildEditorReview("", "글", "승기").user).toBe("[검수할 글]\n글");
    expect(revisionFromReview({ verdict: "수정 후 발행", notes: ["a", "b"] })).toBe("편집자 검수 의견대로 고쳐 주세요. 의견과 관계없는 부분은 그대로 둡니다.\n- a\n- b");
    expect(keywordRequest("", "TCP")).toBe('핵심 검색 키워드 "TCP"를 제목 앞쪽과 첫 문단에 자연스럽게 넣어 주세요.');
    expect(keywordRequest("짧게", " ")).toBe("짧게");
  });
});

describe("guides (블로그 준비 가이드, 데스크톱 BlogGuides와 같은 규칙)", () => {
  const themes = loadThemes();

  it("실제 가이드의 {theme.*} 자리를 테마 값으로 채운다", () => {
    for (const slug of ["tech", "life"]) {
      const theme = themes[slug];
      const guides = loadGuides(theme);
      expect(guides.map((g) => g.key)).toEqual(theme.design.guides);
      const naver = guides.find((g) => g.key === "naver")!;
      expect(naver.markdown).not.toContain("{theme.");
      expect(naver.markdown).toContain(theme.design.categories.join(" / "));
      expect(naver.markdown).toContain(theme.design.accent);
      expect(naver.markdown).toContain(theme.design.layout);
      expect(naver.markdown).not.toContain("<!--");
    }
  });

  it("css·html 블록만 복사 버튼으로 뽑는다", () => {
    const tistory = loadGuides(themes.tech).find((g) => g.key === "tistory")!;
    expect(tistory.title).toBe("티스토리 개설·디자인 가이드");
    expect(tistory.codeBlocks.some((b) => b.language === "css" && b.code.includes(".article-view h2"))).toBe(true);
    expect(tistory.codeBlocks.some((b) => b.language === "html" && b.code.includes("highlight.min.js"))).toBe(true);
    expect(tistory.codeBlocks.every((b) => !b.code.includes("```"))).toBe(true);
    expect(tistory.codeBlocks.find((b) => b.language === "css")!.label.startsWith("CSS · ")).toBe(true);
  });

  it("모르는 이름·빈 값은 그대로 두고 CRLF·머리말·주석을 처리한다", () => {
    const theme = { ...themes.tech, name: "테스트", design: { accent: "#123456", layout: "", categories: [], tistorySkin: "", thumbnail: "", guides: [] } };
    const text = "---\r\ntitle: \"샘플\"\r\n---\r\n<!-- 메모 -->\r\n색 {theme.accent}, 카테고리 {theme.categories}, {theme.unknown}\r\n\r\n```css\r\na{color:red}\r\n```\r\n\r\n```python\r\nprint(1)\r\n```\r\n";
    const guide = parseGuide("sample", text, theme);
    expect(guide.title).toBe("샘플");
    expect(guide.markdown.startsWith("색 #123456, 카테고리 {theme.categories}, {theme.unknown}")).toBe(true);
    expect(guide.codeBlocks).toHaveLength(1);
    expect(guide.codeBlocks[0].code).toBe("a{color:red}");
    expect(fillGuide("{theme.name}", theme)).toBe("테스트");
  });

  it("없는 파일은 건너뛰고, guides가 비면 기본 순서", () => {
    const theme = { ...themes.tech, design: { ...themes.tech.design, guides: [] } };
    const files = { design: "---\ntitle: 디자인\n---\n본문", naver: "네이버" };
    expect(loadGuides(theme, files).map((g) => g.key)).toEqual(["naver", "design"]);
    const custom = { ...themes.tech, design: { ...themes.tech.design, guides: ["design", "missing"] } };
    expect(loadGuides(custom, files).map((g) => g.title)).toEqual(["디자인"]);
  });
});
