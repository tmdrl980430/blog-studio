# BlogStudio.Windows

WPF (.NET 8) 데스크톱 앱. 오늘의 주제로 **내 PC의 Claude Code(Claude Pro 구독)** 가 글을 쓰고, WebView2로 미리 보고, 네이버·티스토리에 붙여넣을 수 있게 복사합니다.

## 준비

실행만 할 때는 따로 설치할 것이 없습니다.

- **.NET**: 배포용 exe에 함께 들어 있습니다 (self-contained 단일 파일).
- **Claude Code**: 없으면 앱 화면에 안내 카드가 뜨고, "자동 설치"를 누르면 공식 설치 스크립트가 실행된 뒤 로그인 창이 열립니다.
- **WebView2 런타임**: Windows 10/11에는 보통 이미 있습니다. 없으면 앱이 Microsoft 설치 페이지로 안내합니다.

코드를 직접 빌드할 때만 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다.

## 실행

```
dotnet run --project apps/desktop/BlogStudio.Windows
```

또는 GitHub Actions 실행 결과의 **Artifacts → BlogStudio-win-x64** 를 내려받아 `BlogStudio.exe` 실행 (`shared` 폴더와 같은 위치에 두기).

## 동작

- 요일별 트랙과 진행 위치로 오늘의 주제를 고릅니다 (`shared/themes/tech`).
- "글 생성하기"를 누르면 빈 임시 폴더에서 `claude -p --output-format text --model sonnet` 을 실행하고, 프롬프트는 표준 입력으로 넘깁니다.
  `ANTHROPIC_API_KEY` 환경 변수는 자식 프로세스에서 지워 API 과금으로 바뀌지 않게 합니다.
- 결과는 `%APPDATA%\BlogStudio\posts\*.md` 에 저장하고, 진행 위치·기록은 `state.json`, 설정은 `settings.json`.
- "본문 복사 + 네이버"는 서식 있는 HTML(CF_HTML)과 마크다운을 함께 클립보드에 넣고 글쓰기 화면을 엽니다.

로그인(Supabase)·휴대폰 요청 대기열·자동 업데이트는 다음 단계에서 붙입니다.
