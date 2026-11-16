import { describe, expect, it, vi } from "vitest";
import { generateOpenAi, isReasoningModel, OpenAiError } from "../lib/openai";
import { generateWith, providerReady, historyLimit } from "../lib/ai";
import { DEFAULT_SETTINGS, exportJson, type Settings } from "../lib/storage";
import { buildPrompt } from "../lib/prompt";
import { loadThemes, loadBasePrompt } from "../lib/themes";

const prompt = { system: "S", user: "U" };
const ok = (body: object) => vi.fn(async () => new Response(JSON.stringify(body)));

describe("OpenAI", () => {
  it("Responses API로 지시문·입력·추론 정도를 보낸다", async () => {
    const fetchImpl = ok({ model: "gpt-5.4-mini", output: [{ type: "message", content: [{ type: "output_text", text: "글" }] }], usage: { input_tokens: 9, output_tokens: 3 } });
    const r = await generateOpenAi(" sk-x ", "gpt-5.4-mini", prompt, { fetchImpl });
    expect(r).toEqual({ text: "글", model: "gpt-5.4-mini", inputTokens: 9, outputTokens: 3 });
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("https://api.openai.com/v1/responses");
    expect((init.headers as Record<string, string>).authorization).toBe("Bearer sk-x");
    const body = JSON.parse(init.body as string);
    expect(body).toMatchObject({ model: "gpt-5.4-mini", instructions: "S", input: "U", store: false, reasoning: { effort: "low" } });
  });
  it("추론 모델이 아니면 reasoning을 빼고, output_text가 있으면 그걸 쓴다", async () => {
    const fetchImpl = ok({ output_text: "답", usage: {} });
    expect((await generateOpenAi("k", "gpt-4.1-mini", prompt, { fetchImpl })).text).toBe("답");
    expect(JSON.parse((fetchImpl.mock.calls[0] as unknown as [string, RequestInit])[1].body as string).reasoning).toBeUndefined();
    expect(isReasoningModel("o4-mini")).toBe(true);
  });
  it("키·모델·잘림 오류를 알기 쉽게", async () => {
    await expect(generateOpenAi("", "m", prompt)).rejects.toThrow(OpenAiError);
    const e401 = vi.fn(async () => new Response("{}", { status: 401 }));
    await expect(generateOpenAi("k", "m", prompt, { fetchImpl: e401 })).rejects.toThrow("키가 맞지 않아요");
    const e404 = vi.fn(async () => new Response("{}", { status: 404 }));
    await expect(generateOpenAi("k", "gpt-x", prompt, { fetchImpl: e404 })).rejects.toThrow('모델 "gpt-x"');
    const cut = ok({ output: [], incomplete_details: { reason: "max_output_tokens" } });
    await expect(generateOpenAi("k", "m", prompt, { fetchImpl: cut })).rejects.toThrow("잘렸어요");
  });
});

describe("AI 고르기·절약", () => {
  const s = (patch: Partial<Settings>): Settings => ({ ...DEFAULT_SETTINGS, ...patch });

  it("설정한 AI의 키가 있어야 준비됨", () => {
    expect(providerReady(s({ apiKey: "k" }))).toBe(true);
    expect(providerReady(s({ provider: "openai", apiKey: "k" }))).toBe(false);
    expect(providerReady(s({ provider: "openai", openaiKey: "o" }))).toBe(true);
  });

  it("OpenAI로 보내고, 절약 모드면 추론 low", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockImplementation(async () => new Response(JSON.stringify({ output_text: "t" })));
    await generateWith(s({ provider: "openai", openaiKey: "o", saveUsage: false }), prompt, "main");
    expect(JSON.parse((fetchSpy.mock.calls[0][1] as RequestInit).body as string).reasoning.effort).toBe("medium");
    await generateWith(s({ provider: "openai", openaiKey: "o", saveUsage: false }), prompt, "light");
    expect(JSON.parse((fetchSpy.mock.calls[1][1] as RequestInit).body as string).reasoning.effort).toBe("low");
    fetchSpy.mockRestore();
  });

  it("절약 모드는 이전 글 목록을 8개로, 내보내기엔 두 키 모두 빠짐", () => {
    expect(historyLimit(s({ saveUsage: true }))).toBe(8);
    expect(historyLimit(s({ saveUsage: false }))).toBe(15);
    const themes = loadThemes();
    const history = Array.from({ length: 20 }, (_, i) => ({ date: `2026-10-${String(i + 1).padStart(2, "0")}`, track: "cs", title: `글${i}` }));
    const p = buildPrompt(loadBasePrompt(), themes.tech, themes.tech.curriculum.cs, "t", history, "", { historyLimit: 8 });
    expect(p.user.match(/^- 2026-/gm)?.length).toBe(8);
    const json = exportJson({ settings: s({ apiKey: "sk-ant", openaiKey: "sk-oa" }), progress: {}, posts: [] });
    expect(json).not.toContain("sk-ant");
    expect(json).not.toContain("sk-oa");
  });
});
