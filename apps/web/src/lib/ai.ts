import type { PromptPair } from "./prompt";
import type { Settings } from "./storage";
import { generate as generateClaude } from "./claude";
import { generateOpenAi } from "./openai";

export type Tier = "main" | "light";

/** 설정의 AI(Claude / OpenAI)로 글쓰기. 가벼운 일은 작은 모델·낮은 추론. */
export function generateWith(settings: Settings, prompt: PromptPair, tier: Tier, signal?: AbortSignal) {
  if (settings.provider === "openai")
    return generateOpenAi(settings.openaiKey, settings.openaiModel, prompt, {
      effort: tier === "light" || settings.saveUsage ? "low" : "medium",
      signal,
    });
  return generateClaude(settings.apiKey, tier === "light" ? "haiku" : settings.model, prompt, { signal });
}

export const providerReady = (s: Settings) => (s.provider === "openai" ? !!s.openaiKey.trim() : !!s.apiKey.trim());

/** 절약 모드면 프롬프트에 넣는 이전 글 제목을 줄인다. */
export const historyLimit = (s: Settings) => (s.saveUsage ? 8 : 15);
