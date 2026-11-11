using YamlDotNet.Serialization;

namespace BlogStudio.Core.Themes;

/// <summary>shared/themes/&lt;slug&gt;/theme.yaml 한 개. 글의 말투·구조·주제 결정 방식을 담는다.</summary>
public sealed class ThemePack
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Description { get; set; } = "";

    /// <summary>curriculum: 트랙별 주제 목록을 순서대로 / queue: 사용자가 등록한 대기 목록.</summary>
    public string TopicSource { get; set; } = "curriculum";

    public string Tone { get; set; } = "";
    public string Length { get; set; } = "";

    /// <summary>main = 주력 모델, light = 가벼운 모델.</summary>
    public string ModelTier { get; set; } = "main";

    public List<string> Structure { get; set; } = new();
    public List<string> Rules { get; set; } = new();
    public List<string> Tracks { get; set; } = new();

    /// <summary>mon~sun → 트랙 키. "rest"는 쉬는 날.</summary>
    public Dictionary<string, string> DefaultSchedule { get; set; } = new();

    [YamlIgnore]
    public Dictionary<string, Track> Curriculum { get; set; } = new();
}

/// <summary>shared/themes/&lt;slug&gt;/curriculum/&lt;key&gt;.yaml 한 개.</summary>
public sealed class Track
{
    [YamlIgnore]
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";
    public string Guide { get; set; } = "";
    public List<string> Topics { get; set; } = new();
}
