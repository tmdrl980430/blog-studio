import { describe, expect, it, beforeEach } from "vitest";
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
});
