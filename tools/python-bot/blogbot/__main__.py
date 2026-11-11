"""사용법:  python -m blogbot [--track cs] [--topic "주제"] [--extra "요청"] [--force] [--no-open] [--list]"""
from __future__ import annotations

import argparse
import datetime as dt
import re
import sys
import webbrowser
from pathlib import Path

from dotenv import load_dotenv

from .core import ROOT, State, load_config, load_track, track_for_today
from .render import build_preview, md_to_html
from .writer import write_post


def slugify(s: str) -> str:
    s = re.sub(r"[\\/:*?\"<>|]", "", s)
    return re.sub(r"\s+", "_", s).strip("_")[:40] or "post"


def save(post, config, today: dt.date) -> Path:
    out = ROOT / config.get("output_dir", "output") / f"{today.isoformat()}_{post.track.key}_{slugify(post.title)}"
    out.mkdir(parents=True, exist_ok=True)
    tag_line = " ".join(f"#{t}" for t in post.tags)
    md_full = f"{post.body_md}\n\n---\n{tag_line}\n"
    html_body = md_to_html(post.body_md)
    (out / "post.md").write_text(
        f"---\ntitle: \"{post.title}\"\ntags: [{', '.join(post.tags)}]\nsummary: \"{post.summary}\"\n"
        f"date: {today.isoformat()}\n---\n\n{md_full}", encoding="utf-8")
    (out / "body.html").write_text(html_body, encoding="utf-8")
    (out / "preview.html").write_text(build_preview(post, config.get("blogs"), md_full, html_body), encoding="utf-8")
    return out


def show_list(config, state):
    for key in sorted(p.stem for p in (ROOT / "curriculum").glob("*.yaml")):
        t = load_track(key)
        i = state.next_index(key)
        nxt = t.topics[i] if i < len(t.topics) else "(커리큘럼 완료 → 자동 심화 주제)"
        print(f"[{key}] {t.name}: {min(i, len(t.topics))}/{len(t.topics)} 완료 · 다음: {nxt}")
    print("\n오늘 요일 트랙:", track_for_today(config, dt.date.today()) or "쉬는 날")


def check_api(config):
    import os

    if config.get("engine", "claude-code") != "api":
        from .writer import call_claude_code

        print("Claude Code(구독)로 연결 확인 중...")
        text, _ = call_claude_code(config, "", "'연결 성공'이라고만 답해줘")
        print("✅ 연결 성공:", text.strip()[:50])
        return

    import anthropic

    key = os.environ.get("ANTHROPIC_API_KEY", "")
    if not key or "여기에" in key:
        print("❌ .env 파일에 ANTHROPIC_API_KEY가 없어요. 가이드의 'API 키 연결'을 확인하세요.")
        return
    print(f"키 확인: {key[:10]}...{key[-4:]}  모델: {config.get('model')}")
    try:
        r = anthropic.Anthropic().messages.create(
            model=config.get("model", "claude-sonnet-5-5"), max_tokens=20,
            messages=[{"role": "user", "content": "'연결 성공'이라고만 답해줘"}])
        print("✅ 연결 성공:", "".join(b.text for b in r.content if b.type == "text").strip())
    except anthropic.AuthenticationError:
        print("❌ 키가 잘못됐어요(401). 콘솔에서 키를 다시 복사해 .env에 붙여넣으세요.")
    except anthropic.PermissionDeniedError as e:
        print("❌ 권한 오류(403):", e.message)
    except anthropic.NotFoundError:
        print("❌ 모델 이름을 찾을 수 없어요(404). config.yaml의 model 값을 확인하세요.")
    except anthropic.BadRequestError as e:
        print("❌ 요청 거부(400):", e.message, "\n   크레딧 잔액이 부족하면 이 오류가 납니다. 콘솔 Billing에서 충전하세요.")
    except anthropic.APIConnectionError:
        print("❌ 인터넷 연결 또는 회사 방화벽/프록시 문제로 API에 접속하지 못했어요.")


def main(argv=None):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(prog="blogbot", description="오늘 공부할 주제로 기술 블로그 글을 만들어 줍니다.")
    ap.add_argument("--track", help="트랙 직접 지정 (algorithm / cs / csharp_windows ...)")
    ap.add_argument("--topic", help="주제 직접 지정 (이 경우 커리큘럼 진행 위치는 그대로)")
    ap.add_argument("--extra", default="", help="이번 글에만 적용할 추가 요청")
    ap.add_argument("--force", action="store_true", help="오늘 이미 썼거나 쉬는 날이어도 작성")
    ap.add_argument("--no-open", action="store_true", help="미리보기 창 열지 않기")
    ap.add_argument("--list", action="store_true", help="트랙별 진행 상황 보기")
    ap.add_argument("--check", action="store_true", help="API 키 연결 확인 (아주 짧은 요청 1회)")
    args = ap.parse_args(argv)

    load_dotenv(ROOT / ".env")
    config = load_config()
    state = State()
    today = dt.date.today()

    if args.list:
        return show_list(config, state)
    if args.check:
        return check_api(config)

    track_key = args.track or track_for_today(config, today)
    if not track_key:
        if not args.force:
            print("오늘은 쉬는 날이에요. (--track 으로 직접 지정 가능)")
            return
        track_key = "algorithm"
    if not args.force and not args.track and state.written_today(today):
        print("오늘 글은 이미 만들었어요:", state.written_today(today)[-1]["folder"], "(--force 로 다시 작성)")
        return

    track = load_track(track_key)
    advance = args.topic is None
    if args.topic:
        topic = args.topic
    else:
        i = state.next_index(track_key)
        topic = track.topics[i] if i < len(track.topics) else None

    print(f"✍  [{track.name}] {topic or '자동 심화 주제'} — 작성 중... (1~2분 걸려요)")
    post = write_post(config, track, topic, state.history(track_key), args.extra)
    out = save(post, config, today)
    state.record(track_key, post.topic, post.title, out.name, today, advance)

    print(f"✅ 완료: {post.title}")
    print(f"   폴더: {out}")
    if post.usage:
        print(f"   토큰: 입력 {post.usage['input_tokens']:,} / 출력 {post.usage['output_tokens']:,}")
    if config.get("open_preview", True) and not args.no_open:
        webbrowser.open((out / "preview.html").as_uri())


if __name__ == "__main__":
    main()
