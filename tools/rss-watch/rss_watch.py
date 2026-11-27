"""블로그 RSS로 마지막 발행 시각을 확인한다 (앱이 꺼져 있던 날을 클라우드에서 알아차리기 위함).

사용:
  python rss_watch.py --naver 블로그ID --tistory 블로그이름 [--hours 26]
  python rss_watch.py --feed https://example.com/rss

결과는 블로그마다 한 줄. 하나라도 --hours 시간 넘게 새 글이 없거나 RSS를 못 읽으면 종료 코드 1.
표준 라이브러리만 쓴다 (설치 없이 클라우드 예약 작업에서 바로 실행).
"""
from __future__ import annotations

import argparse
import sys
import urllib.request
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from urllib.parse import quote

ATOM = "{http://www.w3.org/2005/Atom}"


def naver_feed(blog_id: str) -> str:
    return f"https://rss.blog.naver.com/{quote(blog_id.strip())}.xml"


def tistory_feed(name: str) -> str:
    return f"https://{name.strip()}.tistory.com/rss"


def _parse_date(text: str | None) -> datetime | None:
    if not text or not text.strip():
        return None
    text = text.strip()
    try:
        d = parsedate_to_datetime(text)  # RSS 2.0 (RFC 822)
    except (TypeError, ValueError):
        try:
            d = datetime.fromisoformat(text.replace("Z", "+00:00"))  # Atom
        except ValueError:
            return None
    if d is None:
        return None
    return d if d.tzinfo else d.replace(tzinfo=timezone.utc)


def latest_post(xml_text: str) -> tuple[datetime, str] | None:
    """RSS/Atom 본문에서 가장 최근 글의 (시각, 제목). 글이 없거나 날짜를 못 읽으면 None."""
    root = ET.fromstring(xml_text)
    best: tuple[datetime, str] | None = None
    for item in root.iter("item"):
        d = _parse_date(item.findtext("pubDate"))
        if d and (best is None or d > best[0]):
            best = (d, (item.findtext("title") or "").strip())
    for entry in root.iter(f"{ATOM}entry"):
        d = _parse_date(entry.findtext(f"{ATOM}updated") or entry.findtext(f"{ATOM}published"))
        if d and (best is None or d > best[0]):
            best = (d, (entry.findtext(f"{ATOM}title") or "").strip())
    return best


def check(name: str, xml_text: str, now: datetime, hours: float) -> tuple[bool, str]:
    """(정상 여부, 한 줄 결과)."""
    try:
        post = latest_post(xml_text)
    except ET.ParseError:
        return False, f"{name}: RSS 형식 오류"
    if post is None:
        return False, f"{name}: 글 없음"
    age = (now - post[0]).total_seconds() / 3600
    when = post[0].astimezone(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    if age > hours:
        return False, f"{name}: {age:.0f}시간째 새 글 없음 (마지막 {when} \"{post[1]}\")"
    return True, f"{name}: 정상 (마지막 {when} \"{post[1]}\", {age:.0f}시간 전)"


def fetch(url: str, timeout: float = 20) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": "blog-studio-rss-watch"})
    with urllib.request.urlopen(req, timeout=timeout) as res:
        return res.read().decode("utf-8", errors="replace")


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="블로그 RSS로 마지막 발행 확인")
    p.add_argument("--naver", action="append", default=[], help="네이버 블로그 ID")
    p.add_argument("--tistory", action="append", default=[], help="티스토리 블로그 이름")
    p.add_argument("--feed", action="append", default=[], help="RSS 주소")
    p.add_argument("--hours", type=float, default=26, help="이 시간 넘게 새 글이 없으면 경고 (기본 26)")
    a = p.parse_args(argv)

    targets = [(f"네이버 {x}", naver_feed(x)) for x in a.naver if x.strip()]
    targets += [(f"티스토리 {x}", tistory_feed(x)) for x in a.tistory if x.strip()]
    targets += [(u, u) for u in a.feed if u.strip()]
    if not targets:
        p.error("--naver, --tistory, --feed 중 하나는 필요합니다")

    now = datetime.now(timezone.utc)
    ok_all = True
    for name, url in targets:
        try:
            ok, line = check(name, fetch(url), now, a.hours)
        except Exception as e:  # 네트워크·HTTP 오류
            ok, line = False, f"{name}: RSS를 못 읽음 ({e.__class__.__name__}: {e})"
        ok_all &= ok
        print(("OK   " if ok else "WARN ") + line)
    return 0 if ok_all else 1


if __name__ == "__main__":
    sys.exit(main())
