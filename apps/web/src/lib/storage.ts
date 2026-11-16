import type { PostDraft } from "./posts";

/**
 * 웹 버전 1단계: 모든 데이터는 이 브라우저(localStorage)에만. 서버로 보내지 않는다.
 * API 키도 이 브라우저에만 저장 — 공용 PC에서는 쓰지 말 것 (설정 화면에 안내).
 */
export interface Settings {
  author: string;
  /** 글 쓰는 AI: Claude API 또는 OpenAI API (각자 내 키) */
  provider: "claude" | "openai";
  apiKey: string; // Claude API 키
  model: string; // sonnet | opus | haiku
  openaiKey: string;
  openaiModel: string;
  /** 사용량 절약: 이전 글 목록을 줄이고, OpenAI는 추론을 낮게 */
  saveUsage: boolean;
  naverBlogId: string;
  tistoryName: string;
  blogName: string;
  styleSample: string;
  schedule: Record<string, string>;
}

export interface SavedPost extends PostDraft {
  id: string;
  createdAt: string;
  publishedTo: string[];
}

export interface AppData {
  settings: Settings;
  progress: Record<string, number>;
  posts: SavedPost[]; // 오래된 것부터
}

export const DEFAULT_SETTINGS: Settings = {
  author: "",
  provider: "claude",
  apiKey: "",
  model: "sonnet",
  openaiKey: "",
  openaiModel: "gpt-5.4-mini",
  saveUsage: true,
  naverBlogId: "",
  tistoryName: "",
  blogName: "",
  styleSample: "",
  schedule: {},
};

const KEY = "blog-studio:v1";

export function load(store: Storage = localStorage): AppData {
  try {
    const raw = store.getItem(KEY);
    if (raw) {
      const d = JSON.parse(raw) as Partial<AppData>;
      return {
        settings: { ...DEFAULT_SETTINGS, ...d.settings },
        progress: d.progress ?? {},
        posts: d.posts ?? [],
      };
    }
  } catch {
    // 깨진 데이터는 새로 시작
  }
  return { settings: { ...DEFAULT_SETTINGS }, progress: {}, posts: [] };
}

export function save(data: AppData, store: Storage = localStorage) {
  store.setItem(KEY, JSON.stringify(data));
}

/** 글 저장. 커리큘럼 주제로 쓴 글이면 그 트랙 진도를 한 칸. */
export function addPost(data: AppData, post: PostDraft, advance: boolean): AppData {
  const saved: SavedPost = { ...post, id: crypto.randomUUID(), createdAt: new Date().toISOString(), publishedTo: [] };
  return {
    ...data,
    posts: [...data.posts, saved],
    progress: advance ? { ...data.progress, [post.track]: (data.progress[post.track] ?? 0) + 1 } : data.progress,
  };
}

export function updatePost(data: AppData, id: string, change: Partial<SavedPost>): AppData {
  return { ...data, posts: data.posts.map((p) => (p.id === id ? { ...p, ...change, id } : p)) };
}

/** 내보내기·가져오기 (브라우저를 바꾸거나 데스크톱 앱으로 옮길 때). API 키는 빼고. */
export function exportJson(data: AppData) {
  return JSON.stringify({ ...data, settings: { ...data.settings, apiKey: "", openaiKey: "" } }, null, 2);
}
