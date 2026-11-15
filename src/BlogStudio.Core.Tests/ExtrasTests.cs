using BlogStudio.Core.Insta;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Stats;
using BlogStudio.Core.Storage;
using BlogStudio.Presentation;

namespace BlogStudio.Core.Tests;

public class ExtrasTests
{
    private const string CardJson = """
        좋아요, 카드입니다:
        ```json
        {"cover": "코테 필수\n파이썬 문법", "subtitle": "컴프리헨션부터 sort key까지",
         "slides": [{"heading": "리스트 컴프리헨션", "lines": ["반복문을 한 줄로", "조건도 함께"]},
                    {"heading": "", "lines": ["빈 제목은 버림"]},
                    {"heading": "정렬 key", "lines": ["lambda로 여러 기준 정렬을 깔끔하게 처리하는 방법을 아주 길게 설명하는 줄"]}],
         "caption": "코테 볼 때 꼭 쓰는 파이썬 문법 🐍", "hashtags": ["#코딩테스트", "파이썬 문법", "알고리즘"]}
        ```
        """;

    private static bool IsPng(byte[] b) => b.Length > 8 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';

    [Fact]
    public void Insta_text_parses_from_wrapped_json()
    {
        var text = InstaCards.Parse(CardJson);
        Assert.Equal("코테 필수\n파이썬 문법", text.Cover);
        Assert.Equal(2, text.Slides.Count);
        Assert.EndsWith("#코딩테스트 #파이썬문법 #알고리즘", text.CaptionWithTags);
        Assert.Throws<FormatException>(() => InstaCards.Parse("죄송해요"));
        Assert.Throws<FormatException>(() => InstaCards.Parse("""{"cover": "", "slides": []}"""));
    }

    [Fact]
    public void Insta_cards_render_cover_slides_and_end_at_4x5()
    {
        var text = InstaCards.Parse(CardJson);
        var folder = Directory.CreateTempSubdirectory("bs-insta-").FullName;
        var photo = Path.Combine(folder, "photo.png");
        File.WriteAllBytes(photo, Images.ThumbnailRenderer.RenderPng("x", new[] { "y" }, "z", "#C2410C"));

        foreach (var cover in new[] { photo, null })
        {
            var pages = InstaCards.Render(text, cover, "#C2410C", "승기의 블로그");
            Assert.Equal(4, pages.Count);                                         // 표지 + 2 + 마지막
            Assert.All(pages, p => Assert.True(IsPng(p)));
            Assert.Equal(1080, (pages[0][16] << 24) | (pages[0][17] << 16) | (pages[0][18] << 8) | pages[0][19]);
            Assert.Equal(1350, (pages[0][20] << 24) | (pages[0][21] << 16) | (pages[0][22] << 8) | pages[0][23]);
        }

        var files = InstaCards.Save(folder, InstaCards.Render(text, null, "#000000", "h"), text);
        Assert.Equal(4, files.Count);
        Assert.Contains("#알고리즘", File.ReadAllText(Path.Combine(folder, "insta", "caption.txt")));
        InstaCards.Save(folder, InstaCards.Render(text, null, "#000000", "h").Take(2).ToList(), text);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, "insta"), "*.png").Length);   // 다시 만들면 예전 카드는 지움
    }

    [Fact]
    public void Stats_count_streaks_publishing_and_categories()
    {
        var today = new DateOnly(2026, 11, 15);   // 일요일
        HistoryEntry E(int daysAgo, string track, params string[] to) =>
            new(today.AddDays(-daysAgo), track, "t", "제목", $"{daysAgo}/{track}/post.md", to);
        var history = new List<HistoryEntry>
        {
            E(40, "cs"), E(39, "cs"), E(38, "cs"), E(37, "cs"),                   // 예전 4일 연속
            E(6, "algorithm", "naver"), E(2, "algorithm", "naver", "tistory"), E(1, "food"), E(1, "cs", "tistory"),
        };

        var stats = BlogStats.From(history, today, k => k == "food" ? "맛집·카페" : k);

        Assert.Equal(8, stats.Total);
        Assert.Equal(2, stats.StreakDays);                                         // 오늘은 아직 → 어제, 그제
        Assert.Equal(4, stats.BestStreak);
        Assert.Equal(4, stats.ThisMonth);
        Assert.Equal(4, stats.ThisWeek);                                           // 월(11/9)~일: 9일 1편 + 13일 + 14일 2편
        Assert.Equal(2, stats.PublishedNaver);
        Assert.Equal(2, stats.PublishedTistory);
        Assert.Equal(5, stats.Unpublished);
        Assert.Equal(new CategoryCount("cs", 5, 1), stats.ByCategory[0]);
        Assert.Equal(30, stats.Last30Days.Count);
        Assert.Equal(2, stats.Last30Days[^2].Count);
        Assert.Equal(today, stats.Last30Days[^1].Date);
    }

    [Fact]
    public void Topic_queue_replaces_upcoming_topics_and_follows_progress()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-queue-").FullName);
        var theme = Fixtures.Themes().Load("tech");
        var original = theme.Curriculum["cs"].Topics.ToList();
        store.SavePost(new PostDraft("t", new[] { "a" }, "s", "## b", original[0], "cs", new DateOnly(2026, 11, 1)), true);

        store.SaveTopicQueue("cs", new[] { "  내 주제 A ", "", original[3] });
        store.ApplyTopicQueues(theme);
        Assert.Equal(new[] { original[0], "내 주제 A", original[3] }, theme.Curriculum["cs"].Topics);

        store.SavePost(new PostDraft("t", new[] { "a" }, "s", "## b", "내 주제 A", "cs", new DateOnly(2026, 11, 2)), true);
        Assert.Equal(new[] { original[3] }, store.UpcomingQueue("cs"));
        var again = Fixtures.Themes().Load("tech");
        store.ApplyTopicQueues(again);
        Assert.Equal(original[3], again.Curriculum["cs"].Topics[store.NextIndex("cs")]);

        store.SaveTopicQueue("cs", null);
        Assert.Null(store.UpcomingQueue("cs"));
    }

    [Fact]
    public void Topic_manager_adds_moves_removes_and_saves_schedule()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-tm-").FullName);
        var repo = Fixtures.Themes();
        var vm = new TopicManagerViewModel(store, new[] { repo.Load("tech"), repo.Load("life") });
        Assert.Contains(vm.Tracks, t => t.Key == "food");
        Assert.Equal(7, vm.Schedule.Count);

        vm.SelectedTrack = vm.Tracks.Single(t => t.Key == "algorithm");
        var first = vm.Upcoming[0].Text;
        vm.NewTopic = "다익스트라";
        vm.AddNextCommand.Execute(null);
        vm.NewTopic = "플로이드";
        vm.AddLastCommand.Execute(null);
        Assert.Equal("다익스트라", vm.Upcoming[0].Text);
        vm.MoveDownCommand.Execute(vm.Upcoming[0]);
        Assert.Equal(first, vm.Upcoming[0].Text);
        vm.RemoveCommand.Execute(vm.Upcoming[0]);

        vm.SelectedTrack = vm.Tracks.Single(t => t.Key == "food");               // 다른 트랙 갔다 와도 유지
        vm.SelectedTrack = vm.Tracks.Single(t => t.Key == "algorithm");
        Assert.Equal("다익스트라", vm.Upcoming[0].Text);

        vm.Schedule.Single(d => d.Key == "sun").Selected = TopicManagerViewModel.Rest;
        var saved = false;
        vm.Saved += (_, _) => saved = true;
        vm.SaveCommand.Execute(null);

        Assert.True(saved);
        Assert.Equal("다익스트라", store.UpcomingQueue("algorithm")![0]);
        Assert.Equal("플로이드", store.UpcomingQueue("algorithm")![^1]);
        Assert.Null(store.UpcomingQueue("food"));                                // 안 건드린 트랙은 그대로
        Assert.Equal("rest", store.LoadSettings().Schedule["sun"]);

        var reopened = new TopicManagerViewModel(store, new[] { repo.Load("tech") });
        reopened.SelectedTrack = reopened.Tracks.Single(t => t.Key == "algorithm");
        Assert.Equal("다익스트라", reopened.Upcoming[0].Text);
        reopened.ResetTrackCommand.Execute(null);
        Assert.Equal(first, reopened.Upcoming[0].Text);
        reopened.SaveCommand.Execute(null);
        Assert.Null(store.UpcomingQueue("algorithm"));
    }
}
