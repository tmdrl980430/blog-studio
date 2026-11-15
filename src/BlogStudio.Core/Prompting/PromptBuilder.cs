using System.Text;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Themes;

namespace BlogStudio.Core.Prompting;

public sealed record PromptPair(string System, string User);

/// <summary>사용자가 직접 겪은 내용 (후기 글). Memo가 비어 있으면 정보 글.</summary>
public sealed record PostInputs(string Name, string Memo, int PhotoCount)
{
    public bool HasMemo => !string.IsNullOrWhiteSpace(Memo);
}

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
        string author, string? extra = null, PostInputs? inputs = null)
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
            .Append("\n\n[오늘의 주제] ").Append(
                inputs?.HasMemo == true
                    ? $"아래 [내 메모]의 경험을 바탕으로 한 후기{(string.IsNullOrWhiteSpace(inputs.Name) ? "" : $" — {inputs.Name.Trim()}")}"
                    : string.IsNullOrWhiteSpace(topic) ? AutoTopic : topic)
            .Append("\n\n[이 트랙에서 이전에 쓴 글 (최근 순)]\n")
            .Append(recent.Count > 0 ? string.Join("\n", recent) : "(없음 — 이 트랙의 첫 글)")
            .Append("\n\n이전 글과 내용이 겹치지 않게 하고, 자연스럽게 이어지면 본문에서 \"지난 글에서 다룬 ~\"처럼 한 번 언급해도 좋습니다.");
        if (inputs?.HasMemo == true)
            user.Append("\n\n[내 메모 — 이 내용만 사실로 사용]\n").Append(inputs.Memo.Trim());
        if (inputs is { PhotoCount: > 0 })
            user.Append($"\n\n[내 사진] {inputs.PhotoCount}장 — 본문에 [사진 1: 설명] … [사진 {inputs.PhotoCount}: 설명]을 한 줄씩 모두 넣으세요.");
        if (!string.IsNullOrWhiteSpace(extra))
            user.Append("\n[추가 요청] ").Append(extra.Trim());

        return new PromptPair(system.ToString(), user.ToString());
    }

    /// <summary>
    /// 이미 쓴 글을 지시대로 고치기. 새로 쓰는 것보다 짧은 입력으로, 바뀌지 않은 부분은 그대로 둔다.
    /// </summary>
    public static PromptPair BuildRevision(string currentFileText, string instruction, string author) => new(
        $"""
        당신은 한국어 블로그 글의 편집자입니다. 블로그 주인 {(string.IsNullOrWhiteSpace(author) ? "글쓴이" : author)}의 글을 [고칠 점]대로 고칩니다.
        - 출력은 고친 글 전체 하나만: 맨 앞 front matter(--- ~ ---)와 본문. 설명·인사말·코드 펜스로 감싸기 금지.
        - front matter의 topic, track, date 줄은 그대로 둡니다. title·tags·summary는 [고칠 점]이 요구할 때만 바꿉니다.
        - ![…](images/…) 이미지 줄은 지우거나 주소를 바꾸지 말고, 내용에 맞는 위치를 유지합니다.
        - [고칠 점]과 관계없는 문장은 바꾸지 않습니다.
        - 사실을 새로 지어내지 않습니다. 모르는 정보는 "(확인 필요)"로 남깁니다.
        - 표는 마크다운 표, 코드는 언어 태그가 있는 펜스 코드 블록을 씁니다.
        """,
        $"[고칠 점]\n{instruction.Trim()}\n\n[지금 글]\n{currentFileText}");
}
