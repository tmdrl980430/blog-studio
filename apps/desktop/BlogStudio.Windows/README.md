# BlogStudio.Windows

WPF (.NET 8) 데스크톱 앱. 오늘의 주제로 **내 PC의 Claude Code(Claude Pro 구독)** 가 글을 쓰고, WebView2로 미리 보고, 네이버·티스토리에 붙여넣을 수 있게 복사합니다.

## 준비

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (실행만 할 때는 .NET 8 데스크톱 런타임)
2. Claude Code 설치 후 터미널에서 `claude` 를 한 번 실행해 Claude Pro 계정으로 로그인
3. WebView2 런타임 (Windows 10/11에는 보통 이미 설치돼 있음)

## 실행

```
dotnet run --project apps/desktop/BlogStudio.Windows
```

또는 GitHub Actions 실행 결과의 **Artifacts → BlogStudio-win-x64** 를 내려받아 `BlogStudio.exe` 실행.

## 동작

- 요일별 트랙과 진행 위치로 오늘의 주제를 고릅니다 (`shared/themes/tech`).
- "글 생성하기"를 누르면 빈 임시 폴더에서 `claude -p --output-format text --model sonnet` 을 실행하고, 프롬프트는 표준 입력으로 넘깁니다.
  `ANTHROPIC_API_KEY` 환경 변수는 자식 프로세스에서 지워 API 과금으로 바뀌지 않게 합니다.
- 결과는 `%APPDATA%\BlogStudio\posts\*.md` 에 저장하고, 진행 위치·기록은 `state.json`, 설정은 `settings.json`.
- "본문 복사 + 네이버"는 서식 있는 HTML(CF_HTML)과 마크다운을 함께 클립보드에 넣고 글쓰기 화면을 엽니다.

로그인(Supabase)·휴대폰 요청 대기열·자동 업데이트는 다음 단계에서 붙입니다.
