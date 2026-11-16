# Blog Studio 웹 (1단계)

데스크톱 앱과 같은 테마·프롬프트(`shared/`)로 브라우저에서 글을 씁니다.

- **내 API 키(BYOK)**: Claude API 키를 설정에 넣으면 브라우저에서 바로 Anthropic API를 부릅니다. 키와 글은 이 브라우저(localStorage)에만 저장되고 다른 서버로 가지 않습니다. 쓴 만큼 API 요금이 나갑니다 (Claude Pro 구독과 별개).
- 기술 글(요일별), 맛집·여행·패션·제품 후기/정보 글, 썸네일·그림(PNG), 네이버용 서식 복사, 티스토리 HTML 복사, Claude로 고치기, 지난 글·올림 표시, 백업 내보내기/가져오기.
- 다음 단계: 로그인(Supabase)과 기기 간 동기화, 이미지 자동 업로드.

```bash
cd apps/web
npm install
npm run dev     # http://localhost:5173
npm test
npm run build   # dist/
```

배포: main에 머지하면 `web-pages` 워크플로가 GitHub Pages(`/blog-studio/`)에 올립니다. 처음 한 번 저장소 **Settings → Pages → Source: GitHub Actions**.
