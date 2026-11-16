# staff

앱 안의 AI 직원 지시문. 모든 앱(Windows·Mac·웹)이 같은 파일을 읽는다.
맨 앞 HTML 주석은 설명용이라 프롬프트에서 빠진다.

| 파일 | 직원 | 하는 일 |
|---|---|---|
| `prompts/base.md` | 작가 | 글 쓰기 (기존 공통 규칙) |
| `planner.md` | 기획자 | 오늘 쓸 주제 후보 5개 |
| `editor.md` | 편집자 | 발행 전 검수 의견 (판정 + 고칠 점) |

Claude 스킬 `blog-planner`·`blog-writer`·`blog-editor`와 같은 기준을 쓴다. 한쪽을 고치면 다른 쪽도 맞춘다.
