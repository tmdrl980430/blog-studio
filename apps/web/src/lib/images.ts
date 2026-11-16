/** 브라우저 캔버스로 썸네일(1200×630)과 그림(SVG→PNG) 만들기. */

const FONT = "'Noto Sans KR','Malgun Gothic','Apple SD Gothic Neo',sans-serif";

/** 데스크톱 ThumbnailRenderer.SplitTitle 과 같은 규칙: 첫 줄은 단어 단위로 max자까지, 나머지는 둘째 줄. */
export function splitTitle(title: string, max = 10): string[] {
  title = title.trim();
  if (title.length <= max) return [title];
  let first = "";
  for (const w of title.split(" ").filter(Boolean)) {
    const next = first ? first + " " + w : w;
    if (next.length > max && first) break;
    first = next;
  }
  if (!first || first.length > max + 4) first = title.slice(0, max);
  let rest = title.slice(first.length).trim();
  if (rest.length > max + 4) rest = rest.slice(0, max + 3) + "…";
  return rest ? [first, rest] : [first];
}

export async function renderThumbnail(label: string, lines: string[], footer: string, color: string): Promise<string> {
  const c = document.createElement("canvas");
  c.width = 1200;
  c.height = 630;
  const g = c.getContext("2d");
  if (!g) return "";
  g.fillStyle = color;
  g.fillRect(0, 0, 1200, 630);
  g.fillStyle = "rgba(255,255,255,0.07)";
  g.beginPath();
  g.arc(120, 120, 180, 0, Math.PI * 2);
  g.fill();
  g.fillStyle = "rgba(0,0,0,0.1)";
  g.beginPath();
  g.arc(1060, 720, 210, 0, Math.PI * 2);
  g.fill();

  g.textAlign = "center";
  g.fillStyle = "#fff";
  g.font = `26px ${FONT}`;
  const w = g.measureText(label).width;
  g.fillStyle = "rgba(255,255,255,0.18)";
  g.beginPath();
  g.roundRect((1200 - w) / 2 - 22, 170, w + 44, 44, 22);
  g.fill();
  g.fillStyle = "#fff";
  g.fillText(label, 600, 201);

  let size = 60;
  g.font = `bold ${size}px ${FONT}`;
  while (size > 40 && lines.some((l) => g.measureText(l).width > 620)) g.font = `bold ${(size -= 2)}px ${FONT}`;
  lines.forEach((l, i) => g.fillText(l, 600, 300 + i * size * 1.3));

  g.font = `24px ${FONT}`;
  g.fillStyle = "rgba(255,255,255,0.86)";
  g.fillText(footer, 600, 300 + lines.length * size * 1.3 + 40);
  return c.toDataURL("image/png");
}

export function svgToPng(svg: string, width = 1440): Promise<string> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => {
      const scale = width / (img.naturalWidth || 720);
      const c = document.createElement("canvas");
      c.width = width;
      c.height = Math.round((img.naturalHeight || 400) * scale);
      const g = c.getContext("2d")!;
      g.fillStyle = "#fff";
      g.fillRect(0, 0, c.width, c.height);
      g.drawImage(img, 0, 0, c.width, c.height);
      resolve(c.toDataURL("image/png"));
    };
    img.onerror = () => reject(new Error("그림을 그리지 못했어요"));
    img.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
  });
}

/** data URL 이미지를 클립보드에 (블로그 편집기에 Ctrl+V). */
export async function copyImage(dataUrl: string) {
  const blob = await (await fetch(dataUrl)).blob();
  await navigator.clipboard.write([new ClipboardItem({ [blob.type]: blob })]);
}
