import unittest
from datetime import datetime, timezone

import rss_watch as rw

NOW = datetime(2026, 11, 27, 12, 0, tzinfo=timezone.utc)

RSS = """<?xml version="1.0" encoding="UTF-8"?><rss version="2.0"><channel><title>b</title>
<item><title>옛 글</title><pubDate>Tue, 24 Nov 2026 09:00:00 +0900</pubDate></item>
<item><title>새 글</title><pubDate>Fri, 27 Nov 2026 09:00:00 +0900</pubDate></item>
<item><title>날짜 없음</title></item>
</channel></rss>"""

ATOM = """<feed xmlns="http://www.w3.org/2005/Atom"><title>a</title>
<entry><title>아톰 글</title><updated>2026-11-25T10:00:00Z</updated></entry></feed>"""


class RssWatchTests(unittest.TestCase):
    def test_feed_urls(self):
        self.assertEqual(rw.naver_feed(" tmdrl980430 "), "https://rss.blog.naver.com/tmdrl980430.xml")
        self.assertEqual(rw.tistory_feed("seungki"), "https://seungki.tistory.com/rss")

    def test_latest_rss_picks_newest_regardless_of_order(self):
        d, title = rw.latest_post(RSS)
        self.assertEqual(title, "새 글")
        self.assertEqual(d, datetime(2026, 11, 27, 0, 0, tzinfo=timezone.utc))

    def test_latest_atom(self):
        d, title = rw.latest_post(ATOM)
        self.assertEqual(title, "아톰 글")
        self.assertEqual(d.day, 25)

    def test_recent_post_is_ok(self):
        ok, line = rw.check("네이버 x", RSS, NOW, 26)
        self.assertTrue(ok)
        self.assertIn("정상", line)

    def test_stale_post_warns(self):
        ok, line = rw.check("티스토리 y", RSS, NOW, 6)
        self.assertFalse(ok)
        self.assertIn("12시간째 새 글 없음", line)
        self.assertIn("새 글", line)

    def test_empty_and_broken_feeds_warn(self):
        self.assertEqual(rw.check("n", "<rss><channel></channel></rss>", NOW, 26), (False, "n: 글 없음"))
        self.assertEqual(rw.check("n", "<rss", NOW, 26), (False, "n: RSS 형식 오류"))

    def test_main_requires_target(self):
        with self.assertRaises(SystemExit):
            rw.main([])

    def test_main_exit_code_on_fetch_error(self):
        orig = rw.fetch
        rw.fetch = lambda url, timeout=20: (_ for _ in ()).throw(OSError("offline"))
        try:
            self.assertEqual(rw.main(["--naver", "x"]), 1)
        finally:
            rw.fetch = orig


if __name__ == "__main__":
    unittest.main()
