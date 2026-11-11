# 기술 블로그 자동 작성기

매일 오늘의 공부 주제를 골라 Claude API로 블로그 글을 쓰고, 네이버 블로그·티스토리에
붙여넣을 수 있는 형태로 만들어 줍니다.

```
요일별 트랙 선택 → 커리큘럼의 다음 주제 → Claude가 글 작성 → output/에 저장 → 미리보기 창 열림
                                                            └ [복사] 버튼 → 글쓰기 화면에 Ctrl+V → 발행
```

## 1. 설치 (처음 한 번)

Python 3.10 이상이 필요합니다.

```bash
cd tech-blog-bot
python -m venv .venv
# Windows
.venv\Scripts\pip install -r requirements.txt
# macOS / Linux
.venv/bin/pip install -r requirements.txt
```

1. Claude 연결 방식은 둘 중 하나를 고릅니다. `config.yaml`의 `engine`으로 바꿉니다.
   - **claude-code (기본값)**: 내 Claude 구독(Pro/Max)으로 글을 씁니다. 크레딧 충전이 필요 없습니다.
     [Claude Code](https://docs.claude.com/en/docs/claude-code/setup)를 설치하고, 터미널에서 `claude`를 실행해 한 번 로그인하면 됩니다.
   - **api**: API 키(선불 크레딧)로 글을 씁니다. `.env.example`을 복사해 `.env`로 이름을 바꾸고 키를 넣습니다. 키는 https://platform.claude.com 에서 발급합니다.
   - 어느 쪽이든 설정 후 `run.bat --check`로 연결을 확인합니다.
2. `config.yaml`의 `blogs`에 네이버 블로그 ID와 티스토리 블로그 이름을 넣습니다. 그러면 복사 버튼을 누를 때 글쓰기 화면도 함께 열립니다.

## 2. 사용법

| 명령 | 하는 일 |
|---|---|
| `run.bat` (macOS/Linux는 `./run.sh`) | 오늘 요일의 트랙으로 다음 주제 글 작성 |
| `run.bat --check` | Claude 연결 확인 |
| `run.bat --list` | 트랙별 진행 상황과 다음 주제 확인 |
| `run.bat --track cs` | 요일과 상관없이 특정 트랙으로 작성 |
| `run.bat --track algorithm --topic "BFS 미로 최단거리"` | 원하는 주제로 작성 (커리큘럼 진행 위치는 그대로 유지) |
| `run.bat --extra "예제를 프로그래머스 '게임 맵 최단거리'로"` | 이번 글에만 추가 요청 |
| `run.bat --force` | 오늘 이미 썼거나 쉬는 날이어도 한 편 더 작성 |

생성된 글은 `output/날짜_트랙_제목/` 폴더에 저장됩니다.

- `preview.html`: 미리보기 페이지. 복사 버튼이 여기에 있습니다.
- `post.md`: 마크다운 원본 (GitHub 블로그나 노션에도 사용 가능)
- `body.html`: 인라인 스타일이 적용된 본문 HTML

### 블로그에 올리기
1. 미리보기 페이지에서 **제목 복사**를 누르고 글쓰기 화면의 제목 칸에 붙여넣습니다.
2. **네이버용 / 티스토리용 본문 복사**를 누르면 글쓰기 화면이 열립니다. 본문에 `Ctrl+V` 합니다.
   - 티스토리는 에디터를 **마크다운 모드**로 바꾸고 **마크다운 복사**한 내용을 붙여넣으면 코드 블록이 가장 깔끔합니다.
3. 태그를 붙여넣고 내용을 한 번 읽어 본 뒤 발행합니다.

> 완전 자동 발행을 넣지 않은 이유: 네이버는 2020년 글쓰기 API를 종료했고, 티스토리도 공식 API가 막혀 있습니다.
> 남는 방법은 로그인된 브라우저를 스크립트로 조작하는 것뿐인데, 캡차와 에디터 변경에 쉽게 깨집니다.
> 또 자동 대량 발행은 저품질·어뷰징으로 분류될 위험이 있습니다. 그래서 마지막 클릭은 직접 하도록 했습니다.
> AI가 쓴 글에도 틀린 내용이 있을 수 있으니 발행 전에 코드는 한 번 실행해 보세요.

## 3. 매일 자동 실행

**Windows (작업 스케줄러)**
1. 작업 스케줄러에서 **기본 작업 만들기**를 고르고 트리거를 **매일**로 정합니다. 시간은 예를 들어 오전 7:30입니다.
2. 동작은 **프로그램 시작**으로 하고, 프로그램 칸에 `run.bat`의 전체 경로를 넣습니다. 시작 위치에는 이 폴더 경로를 넣습니다.
3. PC가 켜져 있으면 그 시간에 글이 만들어지고 미리보기가 열립니다. 기본 설정은 쉬는 날 없이 매일 작성합니다.

**macOS / Linux (cron)**: `crontab -e`에 다음 줄을 추가합니다.
```
30 7 * * * /경로/tech-blog-bot/run.sh --no-open >> /경로/tech-blog-bot/cron.log 2>&1
```

## 4. 커스터마이즈

- **주제 목록**: `curriculum/*.yaml`의 `topics`를 고칩니다. 위에서부터 순서대로 진행합니다.
  다 끝나면 Claude가 이전 글을 보고 심화 주제를 직접 골라 이어 씁니다.
- **새 트랙**: `curriculum/새트랙.yaml`을 추가하고 `config.yaml`의 `schedule`에 연결합니다.
- **글 스타일**: `config.yaml`의 `writing`을 고칩니다. 더 세밀하게 바꾸려면 `blogbot/writer.py`의 `SYSTEM_PROMPT`를 수정합니다.
- **모델과 비용**: `config.yaml`의 `model`을 바꿉니다. 실행이 끝나면 토큰 사용량이 출력되니 비용 확인에 참고하세요.
- **진행 초기화**: `state.json`을 지우면 처음부터 다시 진행합니다.
