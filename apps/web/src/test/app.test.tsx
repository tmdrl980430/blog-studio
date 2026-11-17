import { describe, expect, it, beforeEach, afterEach, vi } from "vitest";
import { act } from "react";
import { createRoot } from "react-dom/client";
import App from "../App";

// React 18 act 환경
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe("App", () => {
  beforeEach(() => localStorage.clear());

  it("처음 열면 설정 창, 카테고리 5개, 빈 미리보기", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    await act(async () => createRoot(host).render(<App />));

    expect(host.querySelector('[role="dialog"]')).not.toBeNull();           // API 키가 없으니 설정부터
    const options = [...host.querySelectorAll("aside select option")].map((o) => o.textContent);
    expect(options).toEqual(["기술 공부 · 오늘의 주제", "맛집·카페", "여행·일상", "패션·코디", "제품 리뷰"]);
    expect(host.textContent).toContain("글 만들기");
    expect(host.querySelector(".empty")).not.toBeNull();
  });

  it("일상 카테고리를 고르면 메모 칸이 나온다", async () => {
    localStorage.setItem("blog-studio:v1", JSON.stringify({ settings: { apiKey: "k" }, progress: {}, posts: [] }));
    const host = document.createElement("div");
    document.body.appendChild(host);
    await act(async () => createRoot(host).render(<App />));
    expect(host.querySelector('[role="dialog"]')).toBeNull();

    const select = host.querySelector("aside select") as HTMLSelectElement;
    await act(async () => {
      const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!;
      setter.call(select, "food");
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(host.textContent).toContain("내 메모");
    expect((host.querySelector("aside textarea") as HTMLTextAreaElement).placeholder).toContain("가게 이름");
  });

  describe("AI 직원", () => {
    afterEach(() => vi.unstubAllGlobals());

    const reply = (text: string) =>
      vi.fn(async () => new Response(JSON.stringify({ content: [{ type: "text", text }], usage: {} }), { status: 200 }));
    const click = async (host: HTMLElement, label: string) => {
      const b = [...host.querySelectorAll("button")].find((x) => x.textContent === label)!;
      expect(b, label).toBeTruthy();
      await act(async () => b.click());
    };

    it("기획자 추천 주제를 고르면 오늘 주제가 바뀐다", async () => {
      localStorage.setItem("blog-studio:v1", JSON.stringify({ settings: { apiKey: "k" }, progress: {}, posts: [] }));
      const fetchMock = reply("주제 | 키워드 | 이유 | 카테고리\n1. 성수 브런치 카페 고르는 법 | 성수 브런치 | 검색 많음 | 생활 > 맛집·카페\n2. 서울 디저트 지도 | 디저트 | 시즌 | 없는 카테고리");
      vi.stubGlobal("fetch", fetchMock);
      const host = document.createElement("div");
      document.body.appendChild(host);
      await act(async () => createRoot(host).render(<App />));
      const select = host.querySelector("aside select") as HTMLSelectElement;
      await act(async () => {
        Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, "food");
        select.dispatchEvent(new Event("change", { bubbles: true }));
      });

      await click(host, "기획자에게 주제 추천받기");
      const body = JSON.parse((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].body as string);
      expect(body.model).toContain("haiku");
      expect(body.system[0].text).toContain("주제 기획자");
      expect(body.system[0].text.startsWith("<!--")).toBe(false);
      expect(body.messages[0].content).toContain("[이 트랙 기본 카테고리] 생활 > 맛집·카페");
      const ideas = [...host.querySelectorAll(".ideas button")];
      expect(ideas).toHaveLength(2);
      expect(ideas[1].textContent).not.toContain("없는 카테고리");

      await act(async () => (ideas[0] as HTMLButtonElement).click());
      expect(host.querySelector(".topic")!.textContent).toBe("성수 브런치 카페 고르는 법");
      expect(host.textContent).toContain("기획자 추천 · 성수 브런치 · 생활 > 맛집·카페");
      expect(host.querySelector(".ideas")).toBeNull();
    });

    it("편집자 검수 결과를 보여 주고 의견대로 고치기를 요청한다", async () => {
      const post = {
        id: "p1", createdAt: "2026-11-17T00:00:00Z", publishedTo: [], title: "BFS 정리", tags: ["BFS"], summary: "요약",
        body: "## 본문\n\n내용", topic: "BFS", track: "algorithm", date: "2026-11-17", thumbnail: [],
      };
      localStorage.setItem("blog-studio:v1", JSON.stringify({ settings: { apiKey: "k", author: "승기" }, progress: {}, posts: [post] }));
      const fetchMock = reply("판정: 수정 후 발행\n- 제목 앞에 키워드\n- 첫 문단에 답");
      vi.stubGlobal("fetch", fetchMock);
      const host = document.createElement("div");
      document.body.appendChild(host);
      await act(async () => createRoot(host).render(<App />));
      await act(async () => (host.querySelector(".history button") as HTMLButtonElement).click());

      await click(host, "편집자 검수");
      const body = JSON.parse((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].body as string);
      expect(body.system[0].text).toContain("블로그 주인 승기");
      expect(body.messages[0].content).toContain('title: "BFS 정리"');
      const review = host.querySelector(".review")!;
      expect(review.textContent).toContain("판정: 수정 후 발행");
      expect(review.querySelectorAll("li")).toHaveLength(2);

      fetchMock.mockImplementation(async () =>
        new Response(JSON.stringify({ content: [{ type: "text", text: '---\ntitle: "BFS 최단 거리 정리"\ntags: [BFS]\nsummary: 요약\n---\n\n## 본문\n\n고친 내용' }], usage: {} }), { status: 200 }));
      await click(host, "의견대로 고치기");
      const revise = JSON.parse((fetchMock.mock.calls[1] as unknown as [string, RequestInit])[1].body as string);
      expect(revise.messages[0].content).toContain("- 제목 앞에 키워드\n- 첫 문단에 답");
      expect(host.querySelector("article h1")!.textContent).toBe("BFS 최단 거리 정리");
      expect(host.querySelector(".review")).toBeNull();
    });
  });
});
