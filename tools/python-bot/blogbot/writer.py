"""Claude API로 블로그 글(마크다운)을 생성."""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
from dataclasses import dataclass, field

import yaml

from .core import Track

SYSTEM_PROMPT = """당신은 한국어 기술 블로그 글을 쓰는 작가입니다. 블로그 주인 {author}가 "오늘 공부한 내용"을 정리해 네이버 블로그와 티스토리에 올립니다.

[문체] {tone}
[분량] {length}

[반드시 지킬 것]
- 독자가 이 글 하나만 읽고 오늘 주제를 이해하고 직접 따라 해볼 수 있게 씁니다.
- 코드는 실제로 동작해야 합니다. 언어 태그가 달린 펜스 코드 블록(```python 등)을 사용합니다.
- 사실이 불확실한 내용(버전별 차이, 수치 등)은 단정하지 말고 "버전에 따라 다를 수 있음"처럼 밝힙니다.
- 이모지, 과장된 홍보 문구, "이 글에서는 ~를 알아보겠습니다" 같은 상투적인 서두는 피합니다.
- 제목(#)은 본문에 쓰지 않습니다. 소제목은 ##, ### 만 사용합니다.
- 이미지는 넣지 않습니다. 구조 설명이 필요하면 표나 텍스트 다이어그램(코드 블록)을 씁니다.

[출력 형식] 아래처럼 YAML 머리말 다음에 마크다운 본문만 출력합니다. 다른 말은 덧붙이지 않습니다.
---
title: "검색에 잘 걸리는 구체적인 글 제목 (40자 이내)"
tags: [태그1, 태그2, 태그3, 태그4, 태그5]
summary: "한 줄 요약 (80자 이내)"
---
## 오늘 공부할 내용
(오늘의 학습 목표를 3~5개 체크리스트로: - [ ] 형식)

(본문: 트랙 가이드의 순서를 따름)

## 오늘의 정리
(핵심 3~5줄)

## 스스로 점검하기
(직접 풀어볼 질문이나 연습 문제 2~3개, 답은 <details> 없이 질문만)
"""

USER_PROMPT = """[트랙] {track_name}
[트랙 작성 가이드] {guide}

[오늘의 주제] {topic}

[이 트랙에서 이전에 쓴 글 (최근 순)]
{history}

이전 글과 내용이 겹치지 않게 하고, 자연스럽게 이어지면 본문에서 "지난 글에서 다룬 ~"처럼 한 번 언급해도 좋습니다.
{extra}"""

AUTO_TOPIC = "(커리큘럼의 주제를 모두 다뤘습니다. 이전 글 목록을 보고 다음으로 공부하기 좋은 심화 주제를 직접 하나 골라 작성하세요.)"


@dataclass
class Post:
    title: str
    tags: list[str]
    summary: str
    body_md: str
    topic: str
    track: Track
    usage: dict = field(default_factory=dict)


def parse_post(text: str, topic: str, track: Track) -> Post:
    text = text.strip()
    text = re.sub(r"^```(?:markdown|md)?\s*\n(.*)\n```$", r"\1", text, flags=re.S)  # 통째로 감싼 경우
    m = re.match(r"^---\s*\n(.*?)\n---\s*\n(.*)$", text, flags=re.S)
    if not m:
        raise ValueError("응답에서 YAML 머리말을 찾지 못했어요. 응답 앞부분:\n" + text[:300])
    meta = yaml.safe_load(m.group(1)) or {}
    body = m.group(2).strip()
    tags = meta.get("tags") or []
    if isinstance(tags, str):
        tags = [t.strip() for t in tags.split(",") if t.strip()]
    return Post(
        title=str(meta.get("title") or topic).strip(),
        tags=[str(t).lstrip("#") for t in tags],
        summary=str(meta.get("summary") or "").strip(),
        body_md=body,
        topic=topic,
        track=track,
    )


def build_prompts(config: dict, track: Track, topic: str | None, history: list[dict], extra: str) -> tuple[str, str]:
    w = config.get("writing", {})
    recent = history[-15:][::-1]
    history_txt = "\n".join(f"- {h['date']} {h['title']}" for h in recent) or "(없음 — 이 트랙의 첫 글)"
    system = SYSTEM_PROMPT.format(
        author=w.get("author", "블로그 주인"), tone=w.get("tone", ""), length=w.get("length", "")
    )
    user = USER_PROMPT.format(
        track_name=track.name,
        guide=track.guide,
        topic=topic or AUTO_TOPIC,
        history=history_txt,
        extra=f"\n[추가 요청] {extra}" if extra else "",
    )
    return system, user


def call_api(config: dict, system: str, user: str) -> tuple[str, dict]:
    """Claude API (선불 크레딧) 사용."""
    import anthropic

    if not os.environ.get("ANTHROPIC_API_KEY"):
        raise SystemExit("ANTHROPIC_API_KEY가 없어요. .env 파일에 키를 넣거나 config.yaml의 engine을 claude-code로 바꾸세요.")
    resp = anthropic.Anthropic().messages.create(
        model=config.get("model", "claude-sonnet-5-5"),
        max_tokens=int(config.get("max_tokens", 8000)),
        system=system,
        messages=[{"role": "user", "content": user}],
    )
    if resp.stop_reason == "max_tokens":
        print("⚠ 글이 max_tokens에서 잘렸을 수 있어요. config.yaml의 max_tokens를 늘려 보세요.")
    text = "".join(b.text for b in resp.content if b.type == "text")
    return text, {"input_tokens": resp.usage.input_tokens, "output_tokens": resp.usage.output_tokens}


def find_claude_cli() -> str:
    exe = shutil.which("claude")
    if not exe:
        raise SystemExit(
            "Claude Code(claude 명령)를 찾지 못했어요. 가이드의 'Claude 요금제로 연결'대로 설치하고 로그인해 주세요."
        )
    return exe


def call_claude_code(config: dict, system: str, user: str) -> tuple[str, dict]:
    """설치된 Claude Code를 헤드리스(-p)로 실행 → Claude 구독(Pro/Max) 사용량으로 작성. API 크레딧 불필요."""
    exe = find_claude_cli()
    env = dict(os.environ)
    env.pop("ANTHROPIC_API_KEY", None)  # 키가 있으면 Claude Code가 API 과금으로 바뀌므로 제거
    prompt = (f"{system}\n\n=====\n\n{user}\n\n(파일을 만들거나 도구를 쓰지 말고, 위 출력 형식의 글 본문만 그대로 답하세요.)"
              if system else user)
    with tempfile.TemporaryDirectory() as tmp:  # 빈 폴더에서 실행해 다른 파일을 건드리지 않게
        r = subprocess.run(
            [exe, "-p", "--output-format", "text", "--model", config.get("claude_code_model", "sonnet")],
            input=prompt.encode("utf-8"),
            capture_output=True,
            cwd=tmp,
            env=env,
            timeout=600,
        )
    out = r.stdout.decode("utf-8", errors="replace")
    if r.returncode != 0 or not out.strip():
        err = r.stderr.decode("utf-8", errors="replace") or out
        raise SystemExit(f"Claude Code 실행 실패 (로그인이 됐는지 확인: 터미널에서 claude 실행 후 /login)\n{err[:800]}")
    return out, {}


def write_post(config: dict, track: Track, topic: str | None, history: list[dict], extra: str = "") -> Post:
    system, user = build_prompts(config, track, topic, history, extra)
    engine = config.get("engine", "claude-code")
    text, usage = call_api(config, system, user) if engine == "api" else call_claude_code(config, system, user)
    post = parse_post(text, topic or "(자동 선정)", track)
    if topic is None:
        post.topic = post.title
    post.usage = usage
    return post
