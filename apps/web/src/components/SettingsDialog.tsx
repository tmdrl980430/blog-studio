import { useState } from "react";
import { exportJson, type AppData, type Settings } from "../lib/storage";

interface Props {
  data: AppData;
  onSave: (s: Settings) => void;
  onImport: (d: AppData) => void;
  onClose: () => void;
}

export function SettingsDialog({ data, onSave, onImport, onClose }: Props) {
  const [s, setS] = useState<Settings>(data.settings);
  const set = <K extends keyof Settings>(k: K, v: Settings[K]) => setS({ ...s, [k]: v });

  const download = () => {
    const url = URL.createObjectURL(new Blob([exportJson(data)], { type: "application/json" }));
    const a = Object.assign(document.createElement("a"), { href: url, download: "blog-studio-backup.json" });
    a.click();
    URL.revokeObjectURL(url);
  };
  const upload = async (file: File | undefined) => {
    if (!file) return;
    try {
      const d = JSON.parse(await file.text()) as AppData;
      if (!Array.isArray(d.posts)) throw new Error();
      onImport({ ...d, settings: { ...d.settings, apiKey: s.apiKey, openaiKey: s.openaiKey } });
    } catch {
      alert("블로그 스튜디오 백업 파일이 아니에요");
    }
  };

  return (
    <div className="backdrop" onClick={onClose}>
      <div className="dialog" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="설정">
        <h2>설정</h2>
        <label>작성자 이름<input value={s.author} onChange={(e) => set("author", e.target.value)} /></label>
        <label>
          글 쓰는 AI
          <select value={s.provider} onChange={(e) => set("provider", e.target.value as Settings["provider"])}>
            <option value="claude">Claude (Claude API 키)</option>
            <option value="openai">ChatGPT (OpenAI API 키)</option>
          </select>
          <small>
            웹에서는 구독(Claude Pro·ChatGPT Plus)을 쓸 수 없어서 API 키로 쓰고, 쓴 만큼 요금이 나가요.
            구독으로 쓰려면 Windows 앱을 쓰세요. 키는 이 브라우저에만 저장되고 해당 회사 말고 어디로도 보내지 않아요. 공용 PC에서는 쓰지 마세요.
          </small>
        </label>
        {s.provider === "claude" ? (
          <>
            <label>
              Claude API 키
              <input type="password" value={s.apiKey} placeholder="sk-ant-…" autoComplete="off" onChange={(e) => set("apiKey", e.target.value)} />
              <small>console.anthropic.com → API Keys</small>
            </label>
            <label>
              모델
              <select value={s.model} onChange={(e) => set("model", e.target.value)}>
                <option value="sonnet">Sonnet (권장)</option>
                <option value="opus">Opus (품질↑ 비용↑)</option>
                <option value="haiku">Haiku (가볍고 저렴)</option>
              </select>
            </label>
          </>
        ) : (
          <>
            <label>
              OpenAI API 키
              <input type="password" value={s.openaiKey} placeholder="sk-…" autoComplete="off" onChange={(e) => set("openaiKey", e.target.value)} />
              <small>platform.openai.com → API keys (ChatGPT 구독과 별도 결제)</small>
            </label>
            <label>
              모델
              <input value={s.openaiModel} list="openai-models" onChange={(e) => set("openaiModel", e.target.value)} />
              <datalist id="openai-models">
                <option value="gpt-5.4-mini" />
                <option value="gpt-5.4" />
                <option value="gpt-5.4-nano" />
              </datalist>
              <small>mini 권장 (저렴). 모델 이름은 OpenAI 문서에서 확인해 바꿀 수 있어요.</small>
            </label>
          </>
        )}
        <label className="check-row">
          <input type="checkbox" checked={s.saveUsage} onChange={(e) => set("saveUsage", e.target.checked)} />
          사용량 절약 모드 (권장)
          <small>프롬프트에 넣는 이전 글 목록을 줄이고, ChatGPT는 생각(추론)을 짧게 해요. Claude는 고정 지시문을 캐시해 같은 날 여러 편 쓸 때 더 싸요.</small>
        </label>
        <div className="row">
          <label>네이버 블로그 ID<input value={s.naverBlogId} onChange={(e) => set("naverBlogId", e.target.value)} /></label>
          <label>티스토리 이름<input value={s.tistoryName} onChange={(e) => set("tistoryName", e.target.value)} /></label>
        </div>
        <label>썸네일에 넣을 블로그 이름<input value={s.blogName} onChange={(e) => set("blogName", e.target.value)} /></label>
        <label>
          내 문체 예시 (선택)
          <textarea rows={4} maxLength={1500} value={s.styleSample} onChange={(e) => set("styleSample", e.target.value)} />
          <small>내가 쓴 글 한두 문단. 말투·어미만 따라 해요.</small>
        </label>
        <div className="row">
          <button className="secondary" onClick={download}>백업 내려받기</button>
          <label className="file secondary">
            백업 가져오기
            <input type="file" accept="application/json" onChange={(e) => upload(e.target.files?.[0])} />
          </label>
        </div>
        <div className="actions">
          <button className="secondary" onClick={onClose}>취소</button>
          <button onClick={() => onSave(s)}>저장</button>
        </div>
      </div>
    </div>
  );
}
