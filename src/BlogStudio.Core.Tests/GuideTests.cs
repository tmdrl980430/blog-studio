using BlogStudio.Core.Themes;

namespace BlogStudio.Core.Tests;

public class BlogGuideTests
{
    [Fact]
    public void Loads_guides_in_theme_order_with_titles()
    {
        var repo = Fixtures.Themes();
        var guides = repo.LoadGuides(repo.Load("tech"));

        Assert.Equal(new[] { "naver", "tistory", "design" }, guides.Select(g => g.Key));
        Assert.Equal("티스토리 개설·디자인 가이드", guides[1].Title);
        foreach (var g in guides)
        {
            Assert.DoesNotContain("<!--", g.Markdown);          // 작성자용 주석은 숨김
            Assert.False(g.Markdown.StartsWith("---"));         // 머리말 제거
        }
    }

    [Fact]
    public void Fills_theme_values_into_shared_guides()
    {
        var repo = Fixtures.Themes();
        foreach (var slug in new[] { "tech", "life" })
        {
            var theme = repo.Load(slug);
            var naver = repo.LoadGuides(theme).Single(g => g.Key == "naver");

            Assert.DoesNotContain("{theme.", naver.Markdown);   // 실제 가이드에 남은 자리가 없어야 함
            Assert.Contains(string.Join(" / ", theme.Design.Categories), naver.Markdown);
            Assert.Contains(theme.Design.Accent, naver.Markdown);
            Assert.Contains(theme.Design.Layout, naver.Markdown);
        }
    }

    [Fact]
    public void Extracts_css_and_html_blocks_for_copy_buttons()
    {
        var repo = Fixtures.Themes();
        var tistory = repo.LoadGuides(repo.Load("tech")).Single(g => g.Key == "tistory");

        Assert.Contains(tistory.CodeBlocks, b => b.Language == "css" && b.Code.Contains(".article-view h2"));
        Assert.Contains(tistory.CodeBlocks, b => b.Language == "html" && b.Code.Contains("highlight.min.js"));
        Assert.All(tistory.CodeBlocks, b => Assert.DoesNotContain("```", b.Code));
        Assert.StartsWith("CSS · ", tistory.CodeBlocks.First(b => b.Language == "css").Label);

        var page = tistory.ToHtmlPage();
        Assert.Contains("<h1 class=\"title\">티스토리 개설·디자인 가이드</h1>", page);
        Assert.Contains("<h2 style=", page);
    }

    [Fact]
    public void Parse_keeps_unknown_or_empty_placeholders_and_handles_crlf()
    {
        var theme = new ThemePack { Name = "테스트", Design = new ThemeDesign { Accent = "#123456" } };
        const string text = "---\r\ntitle: \"샘플\"\r\n---\r\n<!-- 메모 -->\r\n색 {theme.accent}, 카테고리 {theme.categories}, {theme.unknown}\r\n\r\n```css\r\na{color:red}\r\n```\r\n\r\n```python\r\nprint(1)\r\n```\r\n";

        var guide = BlogGuides.Parse("sample", text, theme);

        Assert.Equal("샘플", guide.Title);
        Assert.StartsWith("색 #123456, 카테고리 {theme.categories}, {theme.unknown}", guide.Markdown);
        var block = Assert.Single(guide.CodeBlocks);                // python 블록은 복사 버튼 없음
        Assert.Equal("a{color:red}", block.Code);
    }

    [Fact]
    public void Missing_guide_files_are_skipped_and_empty_list_uses_defaults()
    {
        var root = Directory.CreateTempSubdirectory("bs-guides-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "guides"));
        File.WriteAllText(Path.Combine(root, "guides", "naver.md"), "# 네이버");

        var custom = new ThemePack { Design = new ThemeDesign { Guides = new() { "nope", "naver" } } };
        Assert.Equal(new[] { "naver" }, BlogGuides.Load(root, custom).Select(g => g.Key));

        var defaults = BlogGuides.Load(root, new ThemePack());
        Assert.Equal("naver", Assert.Single(defaults).Title);       // 머리말이 없으면 키를 제목으로
    }
}

public class ThemeThumbnailTests
{
    [Fact]
    public void Shared_themes_make_typo_card_thumbnails()
    {
        var repo = Fixtures.Themes();
        foreach (var slug in new[] { "tech", "life" })
            Assert.True(repo.Load(slug).Design.MakesThumbnail);
    }

    [Theory]
    [InlineData("typo_card", true)]
    [InlineData(" Typo_Card ", true)]
    [InlineData("", true)]          // 비어 있으면 기본값(타이포 카드)
    [InlineData(null, true)]
    [InlineData("none", false)]
    [InlineData("photo", false)]    // 아직 모르는 방식은 만들지 않음
    public void Only_typo_card_makes_a_thumbnail(string? value, bool expected)
    {
        Assert.Equal(expected, ThemeDesign.IsTypoCard(value));
        Assert.Equal(expected, new ThemeDesign { Thumbnail = value! }.MakesThumbnail);
    }
}
