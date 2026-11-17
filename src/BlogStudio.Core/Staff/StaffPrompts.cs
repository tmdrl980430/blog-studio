using System.Text;
using System.Text.RegularExpressions;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Themes;

namespace BlogStudio.Core.Staff;

/// <summary>기획자가 낸 주제 후보 하나. Category는 카테고리 목록 안의 값("상위 > 하위") 또는 "" (트랙 기본값을 씀).</summary>
public sealed record TopicIdea(string Topic, string Keyword, string Reason, string Category = "")
{
    public string Label => string.IsNullOrWhiteSpace(Keyword) ? Topic : $"{Topic}  ·  {Keyword}";
}

/// <summary>편집자 검수 결과. Verdict: 발행 가능 / 수정 후 발행 / 다시 쓰기.</summary>
public sealed record EditorReview(string Verdict, IReadOnlyList<string> Notes)
{
    public bool NeedsFix => Notes.Count > 0;
}

/// <summary>
/// 앱 안 AI 직원(shared/staff/*.md)의 프롬프트 조립과 응답 정리.
/// 작가는 기존 PromptBuilder(shared/prompts/base.md)가 맡는다.
/// </summary>
public static class StaffPrompts
{
    public const int IdeaCount = 5;
    public const int TopicMax = 60;
    public const int NoteMax = 7;

    public const string Publishable = "발행 가능", NeedsEdit = "수정 후 발행", Rewrite = "다시 쓰기";

    private const string FallbackPlanner =
        "당신은 한국어 블로그의 주제 기획자입니다. 검색될 만한 주제 5개를 \"주제 | 핵심 검색 키워드 | 이유 | 카테고리\" 형식으로 한 줄에 하나씩만 출력합니다. 카테고리는 [카테고리 목록]에서 그대로 하나 고릅니다.";

    private const string FallbackEditor =
        "당신은 한국어 블로그 글의 편집자입니다. 첫 줄에 \"판정: 발행 가능 | 수정 후 발행 | 다시 쓰기\" 중 하나, 다음 줄부터 \"- 고칠 점\"을 최대 7개 출력합니다.";

    private static readonly Regex ListMark = new(@"^(\d+[.)]\s*|[-*•]\s*)");

    /// <summary>줄 앞 번호("1.", "2)")나 글머리표("-", "*", "•")를 뗀다.</summary>
    internal static string StripListMark(string line) => ListMark.Replace(line, "").Trim();

    /// <summary>
    /// 기획자: 트랙 안에서 오늘 쓸 주제 후보. 이전 글 제목은 최근 15편만 보낸다.
    /// 카테고리 목록(없으면 기본 체계)과 트랙 기본 카테고리를 함께 보내 주제마다 하나를 고르게 한다 (이슈 #19).
    /// </summary>
    public static PromptPair BuildTopicIdeas(string staffPrompt, ThemePack theme, Track track,
        IEnumerable<HistoryEntry> history, string? hint = null, IReadOnlyList<BlogCategory>? categories = null)
    {
        categories ??= BlogCategories.Default;
        var trackDefault = BlogCategories.ForTrack(track.Key, categories);
        var recent = history.Where(h => h.TrackKey == track.Key)
            .OrderByDescending(h => h.Date)
            .Take(PromptBuilder.HistoryLimit)
            .Select(h => "- " + h.Title)
            .ToList();
        var upcoming = track.Topics.Take(10).Select(t => "- " + t).ToList();

        var user = new StringBuilder()
            .Append("[블로그] ").Append(theme.Name)
            .Append("\n[트랙] ").Append(track.Name)
            .Append("\n[트랙 작성 가이드] ").Append(track.Guide.Trim())
            .Append("\n\n[트랙의 기본 주제 목록 (참고, 그대로 베끼지 않음)]\n")
            .Append(upcoming.Count > 0 ? string.Join("\n", upcoming) : "(없음)")
            .Append("\n\n[이전 글]\n")
            .Append(recent.Count > 0 ? string.Join("\n", recent) : "(없음 — 첫 글)")
            .Append("\n\n[카테고리 목록]\n")
            .Append(string.Join("\n", categories.Select(c => "- " + c)))
            .Append("\n[이 트랙 기본 카테고리] ").Append(trackDefault?.ToString() ?? "(없음 — 목록에서 가장 가까운 것)");
        if (!string.IsNullOrWhiteSpace(hint))
            user.Append("\n\n[요청] ").Append(hint.Trim());

        return new PromptPair(string.IsNullOrWhiteSpace(staffPrompt) ? FallbackPlanner : staffPrompt, user.ToString());
    }

    /// <summary>
    /// "주제 | 키워드 | 이유 | 카테고리" 줄을 읽는다. 번호·따옴표 제거, 너무 긴 주제 제외, 중복 제거, 최대 5개.
    /// 카테고리는 목록(없으면 기본 체계) 안의 것으로 맞추고, 목록에 없으면 ""(새로 만들지 않음).
    /// </summary>
    public static IReadOnlyList<TopicIdea> ParseTopicIdeas(string response, IReadOnlyList<BlogCategory>? categories = null) =>
        response.Split('\n')
            .Select(l => ListMark.Replace(l.Trim(), "").Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("주제 |", StringComparison.Ordinal))
            .Select(l => l.Split('|').Select(p => p.Trim().Trim('"', '“', '”', '\'').Trim()).ToArray())
            .Where(p => p[0].Length is > 0 and <= TopicMax)
            .Select(p => new TopicIdea(p[0], p.Length > 1 ? p[1] : "", p.Length > 2 ? p[2] : "",
                p.Length > 3 ? BlogCategories.Match(p[3], categories)?.ToString() ?? "" : ""))
            .DistinctBy(i => i.Topic)
            .Take(IdeaCount)
            .ToList();

    /// <summary>편집자: 저장된 글 전체(front matter 포함)를 보고 검수 의견.</summary>
    public static PromptPair BuildEditorReview(string staffPrompt, string postFileText, string author) => new(
        (string.IsNullOrWhiteSpace(staffPrompt) ? FallbackEditor : staffPrompt)
            .Replace("{author}", string.IsNullOrWhiteSpace(author) ? "글쓴이" : author),
        "[검수할 글]\n" + postFileText);

    /// <summary>"판정: …" 줄과 "- 고칠 점" 줄을 읽는다. 판정이 없으면 고칠 점 유무로 정한다.</summary>
    public static EditorReview ParseEditorReview(string response)
    {
        string? verdict = null;
        var notes = new List<string>();
        foreach (var raw in response.Split('\n'))
        {
            var line = raw.Trim().Trim('*').Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("판정", StringComparison.Ordinal))
            {
                // 형식 안내를 그대로 따라 쓴 줄("발행 가능 | 수정 후 발행 | …")처럼 둘 이상 들어 있으면 판정으로 보지 않음
                var found = new[] { Rewrite, NeedsEdit, Publishable }.Where(v => line.Contains(v)).ToList();
                if (found.Count == 1) verdict ??= found[0];
                continue;
            }
            if (!ListMark.IsMatch(line)) continue;
            var note = ListMark.Replace(line, "").Trim();
            if (note.Length > 0 && !notes.Contains(note)) notes.Add(note);
        }
        if (notes.Count > NoteMax) notes = notes.Take(NoteMax).ToList();
        if (verdict == Publishable && notes.Count > 0) verdict = NeedsEdit;
        return new EditorReview(verdict ?? (notes.Count > 0 ? NeedsEdit : Publishable), notes);
    }

    /// <summary>편집자 의견을 "Claude로 고치기" 요청문으로.</summary>
    public static string RevisionFromReview(EditorReview review) =>
        "편집자 검수 의견대로 고쳐 주세요. 의견과 관계없는 부분은 그대로 둡니다.\n" +
        string.Join("\n", review.Notes.Select(n => "- " + n));
}
