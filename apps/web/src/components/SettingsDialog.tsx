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
      onImport({ ...d, settings: { ...d.settings, apiKey: s.apiKey } });
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
          Claude API 키
          <input type="password" value={s.apiKey} placeholder="sk-ant-…" autoComplete="off" onChange={(e) => set("apiKey", e.target.value)} />
          <small>
            console.anthropic.com에서 만든 내 키. 이 브라우저에만 저장되고 Anthropic 말고 어디로도 보내지 않아요.
            쓴 만큼 API 요금이 나가요 (Claude Pro 구독과 별개). 공용 PC에서는 쓰지 마세요.
          </small>
        </label>
        <label>
          모델
          <select value={s.model} onChange={(e) => set("model", e.target.value)}>
            <option value="sonnet">Sonnet (권장)</option>
            <option value="opus">Opus (품질↑ 비용↑)</option>
            <option value="haiku">Haiku (가볍고 저렴)</option>
          </select>
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
