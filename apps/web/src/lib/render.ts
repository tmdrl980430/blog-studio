import { marked } from "marked";
import DOMPurify from "dompurify";

/** 데스크톱 BlogHtmlRenderer 와 같은 인라인 스타일 (블로그 에디터는 <style>을 버림). */
export const TAG_STYLES: Record<string, string> = {
  h2: "font-size:22px;font-weight:700;margin:36px 0 14px;padding-bottom:6px;border-bottom:2px solid #222;",
  h3: "font-size:18px;font-weight:700;margin:26px 0 10px;",
  h4: "font-size:16px;font-weight:700;margin:20px 0 8px;",
  p: "font-size:16px;line-height:1.8;margin:0 0 14px;",
  li: "font-size:16px;line-height:1.8;margin:2px 0;",
  ul: "margin:0 0 14px;padding-left:22px;",
  ol: "margin:0 0 14px;padding-left:22px;",
  blockquote: "margin:16px 0;padding:10px 16px;border-left:4px solid #3b82f6;background:#f3f7ff;color:#333;",
  table: "border-collapse:collapse;margin:14px 0;font-size:15px;",
  th: "border:1px solid #ccc;background:#f5f5f5;padding:8px 12px;text-align:left;",
  td: "border:1px solid #ccc;padding:8px 12px;",
  hr: "border:none;border-top:1px solid #ddd;margin:28px 0;",
  img: "max-width:100%;height:auto;display:block;margin:16px auto;border-radius:4px;",
};
export const INLINE_CODE =
  "background:#f1f1f1;color:#c7254e;padding:2px 5px;border-radius:4px;font-family:Consolas,monospace;font-size:14px;";
export const CODE_BLOCK =
  "background:#f8f8f8;border:1px solid #e5e5e5;border-radius:6px;padding:14px 16px;overflow-x:auto;" +
  "font-family:Consolas,'D2Coding',monospace;font-size:14px;line-height:1.6;margin:0 0 16px;white-space:pre;";
export const IMAGE_SLOT =
  "border:2px dashed #9bb2f5;background:#f5f8ff;color:#1e3fb0;padding:14px 16px;border-radius:8px;font-size:15px;margin:16px 0;";

export interface Figure {
  number: number; // 이미지 번호 (1 = 썸네일)
  caption: string;
  svg: string;
}

const SVG_BLOCK = /^```svg[ \t]*\r?\n([\s\S]*?)^```[ \t]*\r?$/gm;

/** SVG 그림 블록을 꺼내고 그 자리를 표시 줄로. 이미지 번호는 썸네일(1) 다음부터. */
export function extractFigures(markdown: string, firstNumber = 2): { markdown: string; figures: Figure[] } {
  const figures: Figure[] = [];
  const out = markdown.replace(SVG_BLOCK, (_, svg: string) => {
    const caption = /<!--\s*caption:\s*(.*?)\s*-->/.exec(svg)?.[1] ?? "개념 그림";
    const number = firstNumber + figures.length;
    figures.push({ number, caption, svg: svg.trim() });
    return `\n<p class="bs-figure" data-n="${number}">${escapeHtml(caption)}</p>\n\n`;
  });
  return { markdown: out, figures };
}

export const escapeHtml = (s: string) =>
  s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

const taskBoxes = (md: string) => md.replace(/^(\s*)[-*] \[ \] /gm, "$1- ☐ ").replace(/^(\s*)[-*] \[[xX]\] /gm, "$1- ☑ ");

/** 그림(SVG)을 미리보기용 <img>로 (스크립트는 data: 이미지 안에서 실행되지 않음). */
export const svgDataUrl = (svg: string) => "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);

/**
 * 마크다운 → 블로그용 HTML.
 * mode "copy": 그림·썸네일 자리를 "[이미지 N] 붙여넣기" 표시로, "preview": 실제 그림으로.
 */
export function toBlogHtml(markdown: string, mode: "copy" | "preview", figures: Figure[] = [], thumbnailUrl?: string): string {
  const raw = marked.parse(taskBoxes(markdown.replace(/\r\n/g, "\n")), { async: false, gfm: true }) as string;
  // 모델 출력이라 먼저 소독 (스크립트·이벤트 속성 제거)
  const clean = DOMPurify.sanitize(raw, { ADD_ATTR: ["data-n"] });
  const doc = new DOMParser().parseFromString(`<div id="r">${clean}</div>`, "text/html");
  const root = doc.getElementById("r")!;

  root.querySelectorAll("p.bs-figure").forEach((p) => {
    const n = Number(p.getAttribute("data-n"));
    const fig = figures.find((f) => f.number === n);
    if (mode === "preview" && fig) {
      const wrap = doc.createElement("div");
      wrap.innerHTML = `<img alt="${escapeHtml(fig.caption)}" src="${svgDataUrl(fig.svg)}"><p><em>그림. ${escapeHtml(fig.caption)}</em></p>`;
      p.replaceWith(...Array.from(wrap.childNodes));
    } else {
      p.removeAttribute("class");
      p.setAttribute("style", IMAGE_SLOT);
      p.setAttribute("data-image-slot", "true");
      p.textContent = `[이미지 ${n}: ${p.textContent}] — 이 자리에 이미지 ${n}을 붙여넣으세요`;
    }
  });

  root.querySelectorAll("pre").forEach((pre) => pre.setAttribute("style", CODE_BLOCK));
  root.querySelectorAll("code").forEach((c) => {
    if (c.parentElement?.tagName !== "PRE") c.setAttribute("style", INLINE_CODE);
  });
  for (const [tag, style] of Object.entries(TAG_STYLES))
    root.querySelectorAll(tag).forEach((el) => {
      if (!el.hasAttribute("style")) el.setAttribute("style", style);
    });

  let html = root.innerHTML.trim();
  if (thumbnailUrl !== undefined) {
    html =
      (mode === "preview" && thumbnailUrl
        ? `<img alt="썸네일" src="${thumbnailUrl}" style="${TAG_STYLES.img}">`
        : `<p data-image-slot="true" style="${IMAGE_SLOT}">[이미지 1: 썸네일] — 이 자리에 이미지 1을 붙여넣으세요</p>`) + html;
  }
  return html;
}

/** 네이버 붙여넣기용: 서식 있는 HTML을 클립보드에 (text/html + text/plain). */
export async function copyRich(html: string, plain: string) {
  if ("ClipboardItem" in window) {
    await navigator.clipboard.write([
      new ClipboardItem({
        "text/html": new Blob([html], { type: "text/html" }),
        "text/plain": new Blob([plain], { type: "text/plain" }),
      }),
    ]);
  } else {
    await navigator.clipboard.writeText(plain);
  }
}
