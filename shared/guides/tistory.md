---
platform: tistory
title: 티스토리 개설·디자인 가이드
updated: 2026-11-11
---

<!-- 앱의 "새 채널 만들기 → 블로그 준비" 단계에서 보여 주는 문서. ```css / ```html 블록은 앱에 "복사" 버튼으로 노출. -->

## 1. 블로그 만들기

1. tistory.com → 카카오계정으로 로그인 → 블로그 만들기. 주소(`이름.tistory.com`)는 링크·검색 등록에 그대로 쓰이므로 영문으로 짧게, 처음부터 신중히 정합니다. 이 이름을 블로그 스튜디오 설정에 넣습니다.
2. 관리 → 블로그: 블로그 이름, 닉네임, 설명(주제 키워드 포함), 프로필 아이콘.
3. 관리 → 콘텐츠 → 카테고리 관리: {theme.categories}
4. 글쓰기 화면 오른쪽 위에서 기본 모드 / 마크다운 모드를 바꿀 수 있습니다. 블로그 스튜디오의 "마크다운 복사"는 마크다운 모드에 붙여넣습니다.

## 2. 꾸미기

스킨을 바꾸면 편집 내용이 초기화될 수 있으니 **편집 전에 항상 스킨을 백업**합니다.

1. 꾸미기 → 스킨 변경 → Odyssey(오디세이) 적용. 폰트·배경·위젯은 스킨위자드에서 클릭으로 변경.
2. 꾸미기 → 메뉴: 상단 메뉴를 카테고리 이름과 맞춤.
3. 에디터의 제목1·제목2·제목3 = H2·H3·H4. 블로그 스튜디오의 `##` = 제목1, `###` = 제목2.
4. 꾸미기 → 스킨 편집 → html 편집 → **CSS 탭 맨 아래**에 글 서식 추가 (Odyssey 기준 선택자):

```css
/* 블로그 스튜디오 글 서식 */
.article-view h2 { font-size:22px; font-weight:700; margin:36px 0 14px; padding-bottom:6px; border-bottom:2px solid #222; }
.article-view h3 { font-size:18px; font-weight:700; margin:26px 0 10px; }
.article-view p, .article-view li { font-size:16px; line-height:1.8; }
.article-view :not(pre) > code { background:#f1f1f1; color:#c7254e; padding:2px 5px; border-radius:4px; font-size:14px; }
.article-view blockquote { margin:16px 0; padding:10px 16px; border-left:4px solid #3b82f6; background:#f3f7ff; }
.article-view table th, .article-view table td { border:1px solid #ccc; padding:8px 12px; }
.article-view table th { background:#f5f5f5; }
```

5. 같은 html 편집의 **HTML 탭 `</head>` 바로 앞**에 코드 문법 색(highlight.js) 추가. 블로그 스튜디오의 코드 블록은 `language-python`처럼 언어 클래스가 붙어 있어 자동으로 색이 입혀집니다.

```html
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github.min.css">
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js"></script>
<script>document.addEventListener('DOMContentLoaded', () => hljs.highlightAll());</script>
```


## 3. 글 올리기와 대표 이미지

- 블로그 스튜디오의 "HTML 복사 + 티스토리" → 글쓰기 화면 오른쪽 위 "기본모드"를 **HTML**로 바꾸고 Ctrl+V. 기본모드에 붙이면 표·코드 스타일이 깨집니다.
- 다시 기본모드로 돌아와 "[이미지 N: …]" 줄마다 앱의 이미지 N을 "복사" → Ctrl+V 하고 안내 줄은 지웁니다.
- 사진(Pexels)을 넣었다면 바로 아래 출처 줄은 지우지 마세요.


- 지정하지 않으면 본문 첫 이미지가 대표. 본문 이미지를 클릭하고 "대표"를 누르면 지정.
- 대표 이미지는 내 블로그 목록용이며, 다음·네이버 검색 썸네일로 꼭 쓰이는 것은 아닙니다.

## 4. 검색 노출 (개설 당일)

네이버 서치어드바이저 (searchadvisor.naver.com → 웹마스터 도구):

- [ ] 사이트 등록: `https://이름.tistory.com`
- [ ] 소유 확인: HTML 태그 → 스킨 html 편집의 `<head>` 안에 붙여넣기
- [ ] 요청 → 사이트맵 제출: `https://이름.tistory.com/sitemap.xml`
- [ ] 요청 → RSS 제출: `https://이름.tistory.com/rss`

구글 서치 콘솔 (search.google.com/search-console):

- [ ] URL 접두어로 `https://이름.tistory.com` 추가 → HTML 태그로 소유 확인
- [ ] Sitemaps에 `sitemap.xml` 제출

며칠 뒤 `site:이름.tistory.com`으로 검색해 수집됐는지 확인합니다.
