# rss-watch

PC가 꺼져 있어 앱이 자동 발행을 못 한 날은 앱 안의 경고(auto-log)로 알 수 없다.
그래서 클라우드 예약 작업(RSS 확인 비서)이 블로그 RSS만 보고 "새 글이 없다"를 알린다.

```bash
python tools/rss-watch/rss_watch.py --naver 네이버ID --tistory 티스토리이름 --hours 26
```

- 블로그마다 `OK   …` 또는 `WARN …` 한 줄. 하나라도 `--hours`(기본 26시간) 넘게 새 글이 없거나 RSS를 못 읽으면 종료 코드 1
- RSS 주소: 네이버 `https://rss.blog.naver.com/<ID>.xml`, 티스토리 `https://<이름>.tistory.com/rss` (다른 주소는 `--feed`)
- 표준 라이브러리만 사용 (설치 필요 없음). 테스트: `cd tools/rss-watch && python -m unittest`
- 티스토리는 블로그 관리 → 기본 설정에서 RSS가 켜져 있어야 한다

## 예약 작업 프롬프트 (붙여 넣기용)

자동 발행을 켠 다음, 매일 자동 작성 시각 몇 시간 뒤로 아래 예약 작업을 등록한다. ID·이름은 바꿔 넣는다.

```
블로그 스튜디오 RSS 확인 비서입니다.
1. git clone --depth 1 https://github.com/tmdrl980430/blog-studio /tmp/bs
2. python /tmp/bs/tools/rss-watch/rss_watch.py --naver <네이버ID> --tistory <티스토리이름> --hours 26
3. 종료 코드가 0이면 알림 없이 끝냅니다.
4. 1이면 WARN 줄을 그대로 넣어 알림을 보냅니다. 첫 줄: "블로그 자동 발행이 멈췄을 수 있음 — PC·앱이 켜져 있는지 확인".
```
