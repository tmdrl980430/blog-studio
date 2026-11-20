import { useEffect, useMemo, useRef, useState } from "react";
import { loadThemes, loadBasePrompt, colorFor, type ThemePack, type Track } from "./lib/themes";
import { parsePost, tagLine, toFileText, type PostDraft } from "./lib/posts";
import { matchCategory, resolveCategory } from "./lib/categories";
import {
  blogStats, buildAnalystPrompt, buildEditorReview, buildTopicIdeas, keywordRequest, loadStaffPrompt,
  parseAnalystPriorities, parseEditorReview, parseTopicIdeas, revisionFromReview, trackRecords,
  type EditorReview, type TopicIdea, type TrackPriority,
} from "./lib/staff";
import { buildPrompt, buildRevision } from "./lib/prompt";
import { plan, planTrack, isoDate, DAY_LABELS, dayKey, type PlannedTopic } from "./lib/planner";
import { toBlogHtml, extractFigures, copyRich } from "./lib/render";
import { addPost, load, save, updatePost, type AppData, type SavedPost } from "./lib/storage";
import { generate, ClaudeError } from "./lib/claude";
import { renderThumbnail, splitTitle, svgToPng, copyImage } from "./lib/images";
import { SettingsDialog } from "./components/SettingsDialog";
import { GuideDialog } from "./components/GuideDialog";

const THEMES = loadThemes();
const BASE = loadBasePrompt();
const PLANNER = loadStaffPrompt("planner");
const EDITOR = loadStaffPrompt("editor");
const ANALYST = loadStaffPrompt("analyst");
const TECH = THEMES.tech;
const LIFE = THEMES.life as ThemePack | undefined;

interface Category {
  key: string;
  label: string;
  memo: boolean;
}

const CATEGORIES: Category[] = [
  { key: "tech", label: "기술 공부 · 오늘의 주제", memo: false },
  ...(LIFE ? LIFE.tracks.filter((k) => LIFE.curriculum[k]).map((k) => ({ key: k, label: LIFE.curriculum[k].name, memo: true })) : []),
];

const ANALYST_TRACKS = [TECH, LIFE].flatMap((t) =>
  t ? t.tracks.filter((k) => t.curriculum[k]).map((k) => ({ key: k, name: t.curriculum[k].name })) : [],
);

interface ImageItem {
  number: number;
  label: string;
  url: string;
}

export default function App() {
  const [data, setData] = useState<AppData>(() => load());
  const [category, setCategory] = useState("tech");
  const [memoName, setMemoName] = useState("");
  const [memo, setMemo] = useState("");
  const [extra, setExtra] = useState("");
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState("");
  const [current, setCurrent] = useState<SavedPost | null>(null);
  const [images, setImages] = useState<ImageItem[]>([]);
  const [revision, setRevision] = useState("");
  const [ideas, setIdeas] = useState<TopicIdea[]>([]);
  const [picked, setPicked] = useState<TopicIdea | null>(null); // 기획자 추천 주제 (이번 한 편만)
  const [review, setReview] = useState<EditorReview | null>(null);
  const [analystPicks, setAnalystPicks] = useState<TrackPriority[]>([]);
  // 분석가가 고른 기술 트랙 (요일 일정은 그대로, 그날 이번 한 편만)
  const [trackPick, setTrackPick] = useState<{ key: string; date: string } | null>(null);
  const [showGuide, setShowGuide] = useState(false);
  const [showSettings, setShowSettings] = useState(() => !load().settings.apiKey);
  const abort = useRef<AbortController | null>(null);

  useEffect(() => save(data), [data]);

  const today = isoDate(new Date());
  const cat = CATEGORIES.find((c) => c.key === category)!;
  const analystTrack = !cat.memo && trackPick?.date === today ? TECH.curriculum[trackPick.key] : undefined;
  const planned: PlannedTopic | null = useMemo(() => {
    if (cat.memo && LIFE) return planTrack(LIFE.curriculum[cat.key], today, data.progress);
    if (analystTrack) return planTrack(analystTrack, today, data.progress);
    return plan(TECH, data.settings.schedule, today, data.progress);
  }, [cat, analystTrack, today, data.progress, data.settings.schedule]);
  const theme = cat.memo && LIFE ? LIFE : TECH;

  // 주제 종류·날짜가 바뀌면 기획자 추천은 버린다 / 다른 글을 보면 편집자 의견도 버린다
  const selectCategory = (key: string) => {
    setCategory(key);
    setIdeas([]);
    setPicked(null);
    setTrackPick(null);
  };
  useEffect(() => {
    setIdeas([]);
    setPicked(null);
  }, [today]);
  useEffect(() => setReview(null), [current?.id]);

  // 글이 바뀌면 썸네일·그림 이미지와 미리보기 다시 만들기
  const view = useMemo(() => {
    if (!current) return null;
    const { markdown, figures } = extractFigures(current.body);
    return { markdown, figures };
  }, [current]);

  useEffect(() => {
    let alive = true;
    (async () => {
      if (!current || !view) return setImages([]);
      const color = colorFor(current.track in TECH.curriculum ? TECH : LIFE ?? TECH, current.track);
      const trackName = (TECH.curriculum[current.track] ?? LIFE?.curriculum[current.track])?.name ?? current.track;
      const lines = current.thumbnail.length ? current.thumbnail : splitTitle(current.title);
      const list: ImageItem[] = [
        { number: 1, label: "썸네일", url: await renderThumbnail(trackName, lines, data.settings.blogName || data.settings.author, color) },
      ];
      for (const f of view.figures) {
        try {
          list.push({ number: f.number, label: f.caption, url: await svgToPng(f.svg) });
        } catch {
          // 그릴 수 없는 그림은 건너뜀
        }
      }
      if (alive) setImages(list);
    })();
    return () => {
      alive = false;
    };
  }, [current, view, data.settings.blogName, data.settings.author]);

  const previewHtml = useMemo(
    () => (view ? toBlogHtml(view.markdown, "preview", view.figures, images[0]?.url ?? "") : ""),
    [view, images],
  );
  const copyHtml = useMemo(() => (view ? toBlogHtml(view.markdown, "copy", view.figures, "") : ""), [view]);

  const run = async (work: (signal: AbortSignal) => Promise<void>) => {
    abort.current = new AbortController();
    setBusy(true);
    try {
      await work(abort.current.signal);
    } catch (e) {
      setStatus((e as Error).name === "AbortError" ? "취소했어요" : e instanceof ClaudeError ? e.message : "실패: " + (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const onGenerate = () =>
    run(async (signal) => {
      if (!planned) return;
      setStatus("Claude가 쓰는 중… (1~2분)");
      const fromMemo = cat.memo && memo.trim().length > 0;
      const usePick = !fromMemo && picked !== null;
      const topicText = usePick ? picked.topic : planned.topic;
      const prompt = buildPrompt(
        BASE, theme, planned.track, topicText,
        data.posts.map((p) => ({ date: p.date, track: p.track, title: p.title })),
        data.settings.author,
        {
          extra: usePick ? keywordRequest(extra, picked.keyword) : extra,
          inputs: cat.memo ? { name: memoName, memo } : undefined,
          styleSample: data.settings.styleSample,
        },
      );
      const r = await generate(data.settings.apiKey, data.settings.model, prompt, { signal });
      const topic = fromMemo ? memoName.trim() || "메모 후기" : topicText ?? "(자동 선정)";
      let post: PostDraft = parsePost(r.text, topic, planned.track.key, today);
      if (!fromMemo && !topicText) post = { ...post, topic: post.title };
      // 기획자가 고른 카테고리 > 작가가 적은 카테고리(기본 체계 안) > 트랙 기본값. 데스크톱과 같은 순서
      const written = usePick && matchCategory(picked.category) ? picked.category : post.category;
      post = { ...post, category: resolveCategory(written, post.track) };
      // 기획자 주제는 커리큘럼 진도에 세지 않는다
      const next = addPost(data, post, !fromMemo && planned.topic !== null && !usePick);
      setData(next);
      setCurrent(next.posts.at(-1)!);
      setMemo("");
      setMemoName("");
      setExtra("");
      setIdeas([]);
      setPicked(null);
      setTrackPick(null);
      setAnalystPicks([]);
      setStatus(`완료 · 입력 ${r.inputTokens.toLocaleString()} / 출력 ${r.outputTokens.toLocaleString()} 토큰`);
    });

  const reviseWith = (instruction: string) =>
    run(async (signal) => {
      if (!current || !instruction.trim()) return;
      setStatus("고치는 중…");
      const r = await generate(data.settings.apiKey, data.settings.model, buildRevision(toFileText(current), instruction, data.settings.author), { signal });
      const revised = parsePost(r.text, current.topic, current.track, current.date);
      const next = updatePost(data, current.id, { title: revised.title, tags: revised.tags, summary: revised.summary, body: revised.body });
      setData(next);
      setCurrent(next.posts.find((p) => p.id === current.id)!);
      setRevision("");
      setReview(null);
      setStatus("고쳤어요");
    });
  const onRevise = () => reviseWith(revision);

  const askPlanner = (pack: ThemePack, track: Track, hint: string) =>
    run(async (signal) => {
      setStatus("기획자가 주제를 고르는 중…");
      const prompt = buildTopicIdeas(PLANNER, pack, track, data.posts.map((p) => ({ date: p.date, track: p.track, title: p.title })), hint);
      const r = await generate(data.settings.apiKey, "haiku", prompt, { signal, maxTokens: 1000 });
      const found = parseTopicIdeas(r.text);
      setIdeas(found);
      setStatus(found.length ? "추천 주제를 눌러 오늘 주제로 정하세요" : "기획자가 주제를 내지 못했어요");
    });

  const onAskPlanner = () => {
    if (planned) askPlanner(theme, planned.track, extra);
  };

  const onAskAnalyst = () =>
    run(async (signal) => {
      setAnalystPicks([]);
      setStatus("분석가가 통계를 보는 중…");
      const records = trackRecords(data.posts, ANALYST_TRACKS);
      const prompt = buildAnalystPrompt(ANALYST, blogStats(data.posts, today), records, today, extra);
      const r = await generate(data.settings.apiKey, "haiku", prompt, { signal, maxTokens: 1000 });
      const found = parseAnalystPriorities(r.text, records);
      setAnalystPicks(found);
      setStatus(found.length ? "줄을 누르면 그 트랙으로 기획자에게 주제를 받아요" : "분석가가 우선순위를 내지 못했어요 — 다시 눌러 보세요");
    });

  // 분석가 추천 → 그 트랙으로 옮기고 기획자에게 주제를 받는다. 방향은 '추가 요청'이 비어 있을 때만 넣는다 (데스크톱과 같음)
  const pickAnalyst = (pick: TrackPriority) => {
    const isLife = CATEGORIES.some((c) => c.memo && c.key === pick.trackKey);
    const pack = isLife && LIFE ? LIFE : TECH;
    const track = pack.curriculum[pick.trackKey];
    if (!track) return setStatus(`'${pick.trackName}' 트랙을 찾지 못했어요`);
    selectCategory(isLife ? pick.trackKey : "tech");
    if (!isLife) setTrackPick({ key: pick.trackKey, date: today });
    const hint = extra.trim() ? extra : pick.direction;
    setExtra(hint);
    setAnalystPicks([]);
    askPlanner(pack, track, hint);
  };

  const pickIdea = (idea: TopicIdea) => {
    setPicked(idea);
    setIdeas([]);
    setStatus("오늘 주제를 바꿨어요" + (idea.category ? ` (카테고리 ${idea.category})` : "") + " — 글 만들기를 누르면 작가가 써요");
  };

  const onReview = () =>
    run(async (signal) => {
      if (!current) return;
      setStatus("편집자가 검수하는 중… (1분 안팎)");
      const r = await generate(data.settings.apiKey, data.settings.model, buildEditorReview(EDITOR, toFileText(current), data.settings.author), { signal, maxTokens: 2000 });
      const result = parseEditorReview(r.text);
      setReview(result);
      setStatus(result.notes.length ? `편집자 의견 ${result.notes.length}개 — "의견대로 고치기"로 반영할 수 있어요` : "편집자: 발행해도 좋아요");
    });

  const applyReview = () => {
    if (review?.notes.length) reviseWith(revisionFromReview(review));
  };

  const togglePublished = (blog: string) => {
    if (!current) return;
    const to = current.publishedTo.includes(blog) ? current.publishedTo.filter((b) => b !== blog) : [...current.publishedTo, blog];
    const next = updatePost(data, current.id, { publishedTo: to });
    setData(next);
    setCurrent(next.posts.find((p) => p.id === current.id)!);
  };

  const copy = async (what: "naver" | "tistory" | "markdown" | "tags") => {
    if (!current) return;
    try {
      if (what === "naver") {
        await copyRich(copyHtml, current.body);
        if (data.settings.naverBlogId) window.open(`https://blog.naver.com/${data.settings.naverBlogId}?Redirect=Write`, "_blank");
        setStatus("네이버 글쓰기 본문에 Ctrl+V — [이미지 N] 자리에 이미지를 붙여넣으세요");
      } else if (what === "tistory") {
        await navigator.clipboard.writeText(copyHtml);
        if (data.settings.tistoryName) window.open(`https://${data.settings.tistoryName}.tistory.com/manage/newpost`, "_blank");
        setStatus("티스토리 글쓰기에서 'HTML' 모드로 바꾸고 Ctrl+V");
      } else if (what === "markdown") {
        await navigator.clipboard.writeText(toFileText(current));
        setStatus("마크다운을 복사했어요");
      } else {
        await navigator.clipboard.writeText(`${current.title}\n${tagLine(current)}`);
        setStatus("제목과 태그를 복사했어요");
      }
    } catch {
      setStatus("클립보드에 쓰지 못했어요 — 브라우저 권한을 확인해 주세요");
    }
  };

  const history = [...data.posts].reverse().slice(0, 30);
  const trackName = (k: string) => (TECH.curriculum[k] ?? LIFE?.curriculum[k])?.name ?? k;

  return (
    <div className="app">
      <header>
        <strong>Blog Studio</strong>
        <span className="muted">웹 · 내 API 키로</span>
        <button className="secondary" onClick={() => setShowGuide(true)}>블로그 준비</button>
        <button className="secondary" onClick={() => setShowSettings(true)}>설정</button>
      </header>

      <main>
        <aside>
          <label>
            카테고리
            <select value={category} onChange={(e) => selectCategory(e.target.value)} disabled={busy}>
              {CATEGORIES.map((c) => (
                <option key={c.key} value={c.key}>{c.label}</option>
              ))}
            </select>
          </label>

          <div className="card">
            <div className="muted">{today} {DAY_LABELS[dayKey(today)]}요일{planned ? ` · ${planned.track.name}` : ""}{analystTrack ? " · 분석가 추천" : ""}</div>
            <div className="topic">
              {!planned ? "오늘은 쉬는 날이에요" : picked && !(cat.memo && memo.trim()) ? picked.topic : planned.topic ?? "주제를 다 썼어요 — Claude가 골라요"}
            </div>
            {planned && picked && <div className="muted">기획자 추천{picked.keyword ? ` · ${picked.keyword}` : ""}{picked.category ? ` · ${picked.category}` : ""}</div>}
          </div>
          {planned && (
            <button className="secondary" onClick={onAskPlanner} disabled={busy}>기획자에게 주제 추천받기</button>
          )}
          <button className="secondary" onClick={onAskAnalyst} disabled={busy}>분석가에게 묻기</button>
          {analystPicks.length > 0 && (
            <ul className="ideas analyst">
              {analystPicks.map((p) => (
                <li key={p.trackKey}>
                  <button className="link" onClick={() => pickAnalyst(p)} disabled={busy}>
                    <span>{p.trackName} · {p.direction}</span>
                    {p.reason && <small>{p.reason}</small>}
                  </button>
                </li>
              ))}
            </ul>
          )}
          {ideas.length > 0 && (
            <ul className="ideas">
              {ideas.map((i) => (
                <li key={i.topic}>
                  <button className="link" onClick={() => pickIdea(i)}>
                    <span>{i.topic}</span>
                    <small>{[i.keyword, i.reason, i.category].filter(Boolean).join(" · ")}</small>
                  </button>
                </li>
              ))}
            </ul>
          )}

          {cat.memo && (
            <>
              <label>이름 (가게·장소·제품, 선택)<input value={memoName} onChange={(e) => setMemoName(e.target.value)} /></label>
              <label>
                내 메모 (비우면 위 주제로 정보 글)
                <textarea rows={5} value={memo} placeholder={planned?.track.memoHint} onChange={(e) => setMemo(e.target.value)} />
              </label>
            </>
          )}
          <label>추가 요청 (선택)<input value={extra} onChange={(e) => setExtra(e.target.value)} /></label>

          {busy ? (
            <button className="secondary" onClick={() => abort.current?.abort()}>취소</button>
          ) : (
            <button onClick={onGenerate} disabled={!planned}>글 만들기</button>
          )}
          <p className="status" role="status">{status}</p>

          <h3>지난 글</h3>
          <ul className="history">
            {history.map((p) => (
              <li key={p.id}>
                <button className={p.id === current?.id ? "link active" : "link"} onClick={() => setCurrent(p)}>
                  <span>{p.title}</span>
                  <small>{p.date.slice(5)} · {trackName(p.track)} · {p.publishedTo.length ? p.publishedTo.map((b) => (b === "naver" ? "네이버✓" : "티스토리✓")).join(" ") : "아직 안 올림"}</small>
                </button>
              </li>
            ))}
          </ul>
        </aside>

        <section className="preview">
          {current ? (
            <>
              <div className="toolbar">
                <button onClick={() => copy("naver")}>네이버용 복사</button>
                <button onClick={() => copy("tistory")}>티스토리 HTML 복사</button>
                <button className="secondary" onClick={() => copy("tags")}>제목·태그 복사</button>
                <button className="secondary" onClick={() => copy("markdown")}>마크다운 복사</button>
                <label className="check"><input type="checkbox" checked={current.publishedTo.includes("naver")} onChange={() => togglePublished("naver")} />네이버에 올림</label>
                <label className="check"><input type="checkbox" checked={current.publishedTo.includes("tistory")} onChange={() => togglePublished("tistory")} />티스토리에 올림</label>
              </div>
              <div className="revise">
                <input value={revision} placeholder="고칠 점 (예: 도입부를 두 문장으로)" onChange={(e) => setRevision(e.target.value)} disabled={busy} />
                <button onClick={onRevise} disabled={busy || !revision.trim()}>Claude로 고치기</button>
                <button className="secondary" onClick={onReview} disabled={busy}>편집자 검수</button>
              </div>
              {review && (
                <div className="review" role="region" aria-label="편집자 검수">
                  <strong>판정: {review.verdict}</strong>
                  {review.notes.length > 0 && (
                    <>
                      <ul>{review.notes.map((n) => <li key={n}>{n}</li>)}</ul>
                      <button className="secondary" onClick={applyReview} disabled={busy}>의견대로 고치기</button>
                    </>
                  )}
                </div>
              )}
              {images.length > 0 && (
                <div className="images">
                  {images.map((img) => (
                    <figure key={img.number}>
                      <img src={img.url} alt={img.label} />
                      <figcaption>
                        이미지 {img.number} · {img.label}
                        <button className="secondary" onClick={() => copyImage(img.url).then(() => setStatus(`이미지 ${img.number}을 복사했어요`), () => setStatus("이미지를 복사하지 못했어요 — 저장해서 올려 주세요"))}>복사</button>
                        <a className="button secondary" href={img.url} download={`image-${img.number}.png`}>저장</a>
                      </figcaption>
                    </figure>
                  ))}
                </div>
              )}
              <article>
                <p className="muted">{current.topic} · {current.date}</p>
                <h1>{current.title}</h1>
                {current.category ? <p className="muted">카테고리: {current.category}</p> : null}
                <p className="tags">{tagLine(current)}</p>
                {/* toBlogHtml 안에서 DOMPurify로 소독한 HTML */}
                <div dangerouslySetInnerHTML={{ __html: previewHtml }} />
              </article>
            </>
          ) : (
            <div className="empty">왼쪽에서 "글 만들기"를 누르면 여기에 미리보기가 나와요</div>
          )}
        </section>
      </main>

      {showGuide && (
        <GuideDialog
          themes={LIFE ? [TECH, LIFE] : [TECH]}
          initialSlug={theme.slug}
          onClose={() => setShowGuide(false)}
        />
      )}

      {showSettings && (
        <SettingsDialog
          data={data}
          onClose={() => setShowSettings(false)}
          onSave={(s) => {
            setData({ ...data, settings: s });
            setShowSettings(false);
          }}
          onImport={(d) => {
            setData(d);
            setShowSettings(false);
            setStatus(`백업에서 글 ${d.posts.length}편을 가져왔어요`);
          }}
        />
      )}
    </div>
  );
}
