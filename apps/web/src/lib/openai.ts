import type { PromptPair } from "./prompt";

export class OpenAiError extends Error {}

/** 웹 기본 모델 (설정에서 바꿀 수 있음). */
export const DEFAULT_OPENAI_MODEL = "gpt-5.4-mini";

/** 추론(reasoning) 단계를 지원하는 모델인지 — gpt-5 계열·o 계열. */
export const isReasoningModel = (model: string) => /^(gpt-5|o\d)/i.test(model.trim());

/**
 * 내 OpenAI API 키로 Responses API 호출 (ChatGPT 구독은 브라우저에서 쓸 수 없어서 API 키).
 * instructions(고정 지시문)를 앞에 둬서 OpenAI의 자동 프롬프트 캐시가 잘 맞게 한다.
 */
export async function generateOpenAi(
  apiKey: string,
  model: string,
  prompt: PromptPair,
  opts: { effort?: "low" | "medium"; maxTokens?: number; signal?: AbortSignal; fetchImpl?: typeof fetch } = {},
): Promise<{ text: string; model: string; inputTokens: number; outputTokens: number }> {
  if (!apiKey.trim()) throw new OpenAiError("설정에서 OpenAI API 키를 넣어 주세요");
  const m = model.trim() || DEFAULT_OPENAI_MODEL;
  const res = await (opts.fetchImpl ?? fetch)("https://api.openai.com/v1/responses", {
    method: "POST",
    signal: opts.signal,
    headers: { "content-type": "application/json", authorization: `Bearer ${apiKey.trim()}` },
    body: JSON.stringify({
      model: m,
      instructions: prompt.system,
      input: prompt.user,
      max_output_tokens: opts.maxTokens ?? 12000,
      store: false,
      ...(isReasoningModel(m) ? { reasoning: { effort: opts.effort ?? "low" } } : {}),
    }),
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) {
    const msg = body?.error?.message ?? res.statusText;
    if (res.status === 401) throw new OpenAiError("OpenAI API 키가 맞지 않아요 — 설정에서 확인해 주세요");
    if (res.status === 429) throw new OpenAiError("요청이 많거나 크레딧이 부족해요 (" + msg + ")");
    if (res.status === 404) throw new OpenAiError(`모델 "${m}"을 쓸 수 없어요 — 설정에서 모델 이름을 확인해 주세요`);
    throw new OpenAiError(`OpenAI API 오류 ${res.status}: ${msg}`);
  }
  type Part = { type: string; text?: string };
  const text: string =
    typeof body.output_text === "string" && body.output_text
      ? body.output_text
      : (body.output ?? [])
          .flatMap((o: { content?: Part[] }) => o.content ?? [])
          .filter((c: Part) => c.type === "output_text")
          .map((c: Part) => c.text ?? "")
          .join("");
  if (!text.trim()) {
    const reason = body?.incomplete_details?.reason;
    throw new OpenAiError(reason === "max_output_tokens" ? "답이 너무 길어 잘렸어요 — 다시 시도해 주세요" : "빈 답을 받았어요");
  }
  return { text, model: body.model ?? m, inputTokens: body.usage?.input_tokens ?? 0, outputTokens: body.usage?.output_tokens ?? 0 };
}
