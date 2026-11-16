import type { PromptPair } from "./prompt";

/** Anthropic API 모델 이름 (설정의 sonnet/opus/haiku). */
export const MODELS: Record<string, string> = {
  sonnet: "claude-sonnet-5-5",
  opus: "claude-opus-5-5",
  haiku: "claude-haiku-5-5",
};

export class ClaudeError extends Error {}

/**
 * 내 API 키로 브라우저에서 바로 Claude API 호출 (BYOK). 키는 Anthropic 말고 어디로도 보내지 않는다.
 * system 블록에 프롬프트 캐시를 걸어, 같은 테마로 여러 편 쓸 때 입력 토큰 비용을 줄인다.
 */
export async function generate(
  apiKey: string,
  model: string,
  prompt: PromptPair,
  opts: { maxTokens?: number; signal?: AbortSignal; fetchImpl?: typeof fetch } = {},
): Promise<{ text: string; model: string; inputTokens: number; outputTokens: number }> {
  if (!apiKey.trim()) throw new ClaudeError("설정에서 Claude API 키를 넣어 주세요");
  const res = await (opts.fetchImpl ?? fetch)("https://api.anthropic.com/v1/messages", {
    method: "POST",
    signal: opts.signal,
    headers: {
      "content-type": "application/json",
      "x-api-key": apiKey.trim(),
      "anthropic-version": "2023-06-01",
      "anthropic-dangerous-direct-browser-access": "true",
    },
    body: JSON.stringify({
      model: MODELS[model] ?? model,
      max_tokens: opts.maxTokens ?? 8000,
      system: [{ type: "text", text: prompt.system, cache_control: { type: "ephemeral" } }],
      messages: [{ role: "user", content: prompt.user }],
    }),
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) {
    const msg = body?.error?.message ?? res.statusText;
    if (res.status === 401) throw new ClaudeError("API 키가 맞지 않아요 — 설정에서 확인해 주세요");
    if (res.status === 429) throw new ClaudeError("요청이 많아요. 잠시 후 다시 해 주세요 (" + msg + ")");
    if (res.status === 400 && /credit|balance/i.test(msg)) throw new ClaudeError("API 크레딧이 부족해요 — console.anthropic.com에서 충전");
    throw new ClaudeError(`Claude API 오류 ${res.status}: ${msg}`);
  }
  const text = (body.content ?? []).filter((c: { type: string }) => c.type === "text").map((c: { text: string }) => c.text).join("");
  return {
    text,
    model: body.model ?? model,
    inputTokens: body.usage?.input_tokens ?? 0,
    outputTokens: body.usage?.output_tokens ?? 0,
  };
}
