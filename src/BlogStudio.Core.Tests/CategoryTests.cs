using BlogStudio.Core.Posts;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Storage;

namespace BlogStudio.Core.Tests;

public class CategoryTests
{
    private static PostDraft Post(string category = "") =>
        new("제목", new[] { "태그" }, "요약", "## 본문", "주제", "cs", new DateOnly(2026, 11, 17), Category: category);

    [Theory]
    [InlineData("개발 > CS 기초", "개발", "CS 기초")]
    [InlineData("개발>CS 기초", "개발", "CS 기초")]
    [InlineData(" 생활 / 맛집·카페 ", "생활", "맛집·카페")]
    [InlineData("개발", "개발", "")]
    public void Parses_parent_and_child(string text, string parent, string child)
    {
        var c = BlogCategory.Parse(text)!;
        Assert.Equal(new BlogCategory(parent, child), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_has_no_category(string? text) => Assert.Null(BlogCategory.Parse(text));

    [Fact]
    public void Default_list_matches_issue_and_every_track_has_a_default()
    {
        Assert.Equal(7, BlogCategories.Default.Count);
        Assert.Equal("개발 > 알고리즘·코테", BlogCategories.Default[0].ToString());
        Assert.Equal("생활 > 제품 리뷰", BlogCategories.Default[^1].ToString());

        var themes = Fixtures.Themes();
        foreach (var theme in new[] { themes.Load("tech"), themes.Load("life") })
            foreach (var key in theme.Curriculum.Keys)
                Assert.Contains(BlogCategories.ForTrack(key)!, BlogCategories.Default);
        Assert.Null(BlogCategories.ForTrack("unknown"));
    }

    [Fact]
    public void Match_finds_only_listed_categories_and_never_invents()
    {
        Assert.Equal("개발 > CS 기초", BlogCategories.Match("개발>CS기초")!.ToString());
        Assert.Equal("생활 > 여행·일상", BlogCategories.Match("여행·일상")!.ToString());   // 하위 이름만
        Assert.Null(BlogCategories.Match("개발 > 자바"));
        Assert.Null(BlogCategories.Match("운동"));
    }

    [Fact]
    public void Resolve_prefers_written_listed_category_then_track_default()
    {
        Assert.Equal("개발 > C#·Windows", BlogCategories.Resolve("개발 > C#·Windows", "cs"));
        Assert.Equal("개발 > CS 기초", BlogCategories.Resolve("없는 > 카테고리", "cs"));
        Assert.Equal("생활 > 맛집·카페", BlogCategories.Resolve("", "food"));
        Assert.Equal("", BlogCategories.Resolve("", "unknown"));
    }

    [Fact]
    public void Old_post_without_category_still_parses_and_round_trips_without_category_line()
    {
        var old = "---\ntitle: \"예전 글\"\ntags: [a, b]\nsummary: \"요약\"\ntopic: \"주제\"\ntrack: cs\ndate: 2026-11-01\n---\n\n## 본문\n";
        var post = PostParser.Parse(old, "주제", "cs", new DateOnly(2026, 11, 1));
        Assert.Equal("", post.Category);
        Assert.Equal(old, LocalStore.ToFileText(post));
    }

    [Fact]
    public void Category_is_saved_in_post_file_and_read_back()
    {
        var text = LocalStore.ToFileText(Post("개발 > CS 기초"));
        Assert.Contains("\ncategory: \"개발 > CS 기초\"\n", text);
        var back = PostParser.Parse(text, "주제", "cs", new DateOnly(2026, 11, 17));
        Assert.Equal("개발 > CS 기초", back.Category);
    }

    [Fact]
    public void Saved_post_with_category_reopens_from_store()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-cat-").FullName);
        store.SavePost(Post("생활 > 맛집·카페"), false);
        Assert.Equal("생활 > 맛집·카페", store.LoadLatest()!.Post.Category);
    }

    [Fact]
    public void Revision_prompt_keeps_category_line()
    {
        var p = PromptBuilder.BuildRevision(LocalStore.ToFileText(Post("개발 > CS 기초")), "고쳐 줘", "");
        Assert.Contains("topic, track, category, date 줄은 그대로", p.System);
        Assert.Contains("category: \"개발 > CS 기초\"", p.User);
    }
}
