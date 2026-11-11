using System.Text;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Themes;

namespace BlogStudio.Core.Prompting;

public sealed record PromptPair(string System, string User);

/// <summary>
/// 프롬프트 조립. 토큰 절약을 위해 고정 부분(공통 규칙 → 테마 지시)을 앞에, 매번 바뀌는 부분(주제·이전 글·요청)을 뒤에 둔다.
/// 이전 글은 제목만 최근 15편까지 보낸다.
/// </summary>
public sealed class PromptBuilder
{
    public const int HistoryLimit = 15;
    public const string AutoTopic =
        "(커리큘럼의 주제를 모두 다뤘습니다. 이전 글 목록을 보고 다음으로 공부하기 좋은 심화 주제를 직접 하나 골라 작성하세요.)";

    private readonly string _basePrompt;

    public PromptBuilder(string basePrompt) => _basePrompt = basePrompt;

    public PromptPair Build(ThemePack theme, Track track, string? topic, IEnumerable<HistoryEntry> history,
        string author, string? extra = null)
    {
        var system = new StringBuilder()
            .Append(_basePrompt
                .Replace("{author}", string.IsNullOrWhiteSpace(author) ? "블로그 주인" : author)
                .Replace("{tone}", theme.Tone)
                .Replace("{length}", theme.Length));

        system.Append("\n\n[테마] ").Append(theme.Name);
        if (!string.IsNullOrWhiteSpace(theme.Description)) system.Append(" — ").Append(theme.Description.Trim());
        if (theme.Structure.Count > 0)
        {
            system.Append("\n[글 구조]");
            foreach (var line in theme.Structure) system.Append("\n- ").Append(line);
        }
        if (theme.Rules.Count > 0)
        {
            system.Append("\n[테마 규칙]");
            foreach (var line in theme.Rules) system.Append("\n- ").Append(line);
        }

        var recent = history.Where(h => h.TrackKey == track.Key)
            .OrderByDescending(h => h.Date)
            .Take(HistoryLimit)
            .Select(h => $"- {h.Date:yyyy-MM-dd} {h.Title}")
            .ToList();

        var user = new StringBuilder()
            .Append("[트랙] ").Append(track.Name)
            .Append("\n[트랙 작성 가이드] ").Append(track.Guide.Trim())
            .Append("\n\n[오늘의 주제] ").Append(string.IsNullOrWhiteSpace(topic) ? AutoTopic : topic)
            .Append("\n\n[이 트랙에서 이전에 쓴 글 (최근 순)]\n")
            .Append(recent.Count > 0 ? string.Join("\n", recent) : "(없음 — 이 트랙의 첫 글)")
            .Append("\n\n이전 글과 내용이 겹치지 않게 하고, 자연스럽게 이어지면 본문에서 \"지난 글에서 다룬 ~\"처럼 한 번 언급해도 좋습니다.");
        if (!string.IsNullOrWhiteSpace(extra))
            user.Append("\n[추가 요청] ").Append(extra.Trim());

        return new PromptPair(system.ToString(), user.ToString());
    }
}
