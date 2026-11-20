import { useMemo, useState } from "react";
import { loadGuides, type BlogGuide } from "../lib/guides";
import { toBlogHtml } from "../lib/render";
import type { ThemePack } from "../lib/themes";

interface Props {
  themes: ThemePack[];
  /** 처음 보여 줄 테마 (지금 고른 카테고리의 테마) */
  initialSlug: string;
  onClose: () => void;
  /** 테스트에서 가이드 파일을 바꿔 끼울 때 */
  load?: (theme: ThemePack) => BlogGuide[];
}

/** "블로그 준비" 가이드 창: 문서별 탭, css/html 블록 복사 버튼, {theme.*}는 테마 값으로 채움. */
export function GuideDialog({ themes, initialSlug, onClose, load = loadGuides }: Props) {
  const [slug, setSlug] = useState(themes.some((t) => t.slug === initialSlug) ? initialSlug : themes[0]?.slug ?? "");
  const theme = themes.find((t) => t.slug === slug) ?? themes[0];
  const guides = useMemo(() => (theme ? load(theme) : []), [theme, load]);
  const [key, setKey] = useState("");
  const [copied, setCopied] = useState("");
  const guide = guides.find((g) => g.key === key) ?? guides[0];
  const html = useMemo(() => (guide ? toBlogHtml(guide.markdown, "preview") : ""), [guide]);

  const copy = async (code: string, label: string) => {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(`복사했어요: ${label}`);
    } catch {
      setCopied("복사하지 못했어요 — 코드를 직접 선택해 복사해 주세요");
    }
  };

  return (
    <div className="backdrop" onClick={onClose}>
      <div className="dialog wide" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="블로그 준비">
        <h2>블로그 준비</h2>
        <div className="row">
          {themes.length > 1 && (
            <label>
              테마
              <select value={theme?.slug} onChange={(e) => { setSlug(e.target.value); setKey(""); setCopied(""); }}>
                {themes.map((t) => <option key={t.slug} value={t.slug}>{t.name || t.slug}</option>)}
              </select>
            </label>
          )}
        </div>
        {guides.length === 0 ? (
          <p className="muted">이 테마에는 준비 가이드가 없어요</p>
        ) : (
          <>
            <div className="tabs" role="tablist">
              {guides.map((g) => (
                <button key={g.key} role="tab" aria-selected={g.key === guide?.key}
                  className={g.key === guide?.key ? "link active" : "link"}
                  onClick={() => { setKey(g.key); setCopied(""); }}>
                  {g.title}
                </button>
              ))}
            </div>
            {guide && guide.codeBlocks.length > 0 && (
              <div className="copy-list">
                {guide.codeBlocks.map((b, i) => (
                  <button key={i} className="secondary" onClick={() => copy(b.code, b.label)}>복사 · {b.label}</button>
                ))}
              </div>
            )}
            {copied && <p className="muted">{copied}</p>}
            <div className="preview guide" dangerouslySetInnerHTML={{ __html: html }} />
          </>
        )}
        <div className="actions">
          <button className="secondary" onClick={onClose}>닫기</button>
        </div>
      </div>
    </div>
  );
}
