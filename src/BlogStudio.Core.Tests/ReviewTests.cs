using BlogStudio.Core.Posts;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Review;
using BlogStudio.Core.Storage;

namespace BlogStudio.Core.Tests;

public class ReviewTests
{
    private static PostDraft Post(string title, int tags, string body) => new(
        title, Enumerable.Range(1, tags).Select(i => $"태그{i}").ToList(), "요약", body, "주제", "algorithm", new DateOnly(2026, 11, 14));

    [Fact]
    public void Good_post_passes_every_check()
    {
        var body = "![썸네일](images/00_thumbnail.png)\n\n## 하나\n" + new string('가', 900) + "\n\n## 둘\n| a | b |\n|---|---|\n| 1 | 2 |\n\n## 셋\n" + new string('나', 700);
        var checks = PostChecker.Check(Post("파이썬 코테 필수 문법 정리하기", 6, body));
        Assert.All(checks, c => Assert.Equal(CheckLevel.Ok, c.Level));
        Assert.Contains(checks, c => c.Text == "표 있음");
    }

    [Fact]
    public void Problems_are_flagged()
    {
        var body = "## 가게 정보\n영업시간 (확인 필요)\n[사진 1: 라멘]\n```svg\n<svg/>\n```";
        var warns = PostChecker.Check(Post("이 제목은 검색 결과에서 잘릴 만큼 아주아주아주 길게 쓴 제목입니다 정말로요", 2, body))
            .Where(c => c.Level == CheckLevel.Warn).Select(c => c.Text).ToList();

        Assert.Contains(warns, t => t.StartsWith("제목") && t.Contains("잘려요"));
        Assert.Contains(warns, t => t.StartsWith("태그 2개"));
        Assert.Contains(warns, t => t.StartsWith("본문"));
        Assert.Contains(warns, t => t.StartsWith("소제목 1개"));
        Assert.Contains(warns, t => t.StartsWith("이미지 없음"));
        Assert.Contains(warns, t => t.Contains("'(확인 필요)' 1곳"));
        Assert.Contains(warns, t => t.Contains("자리 2곳"));
    }

    [Fact]
    public void Text_length_ignores_code_images_and_markup()
    {
        Assert.Equal(4, PostChecker.TextLength("## 제목\n![그림](images/a.png)\n```python\nprint(1)\n```\n**본문**"));
        Assert.Equal(2, PostChecker.TextLength("[링크](https://x.y)"));
    }

    [Fact]
    public void Revision_prompt_carries_instruction_and_whole_post()
    {
        var post = Post("제목입니다", 5, "![썸네일](images/00_thumbnail.png)\n\n## 본문");
        var p = PromptBuilder.BuildRevision(LocalStore.ToFileText(post), "  도입부를 줄여 줘 ", "김승기");
        Assert.StartsWith("[고칠 점]\n도입부를 줄여 줘\n\n[지금 글]\n---\ntitle: \"제목입니다\"", p.User);
        Assert.Contains("images/00_thumbnail.png", p.User);
        Assert.Contains("김승기", p.System);
        Assert.Contains("이미지 줄은 지우거나", p.System);
    }

    [Fact]
    public void Update_post_rewrites_file_and_history_title_only()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-upd-").FullName);
        var post = Post("처음 제목", 5, "## 본문");
        var path = store.SavePost(post, advanceProgress: true);
        var file = store.RelativeFileName(path);

        store.UpdatePost(file, post with { Title = "고친 제목", BodyMarkdown = "## 고친 본문" });

        Assert.Contains("고친 본문", File.ReadAllText(path));
        var state = store.LoadState();
        Assert.Equal("고친 제목", state.History.Single().Title);
        Assert.Equal(1, state.Progress["algorithm"]);                          // 진도는 그대로
        Assert.Throws<FileNotFoundException>(() => store.UpdatePost("없는/post.md", post));
    }
}
