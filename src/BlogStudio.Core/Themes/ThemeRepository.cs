using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BlogStudio.Core.Themes;

/// <summary>
/// shared/ 폴더(테마 팩, 공통 프롬프트)를 읽는다. 웹·Mac 앱도 같은 파일을 읽으므로 형식을 바꾸면 모두 영향을 받는다.
/// </summary>
public sealed class ThemeRepository
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public string SharedRoot { get; }

    public ThemeRepository(string sharedRoot)
    {
        if (!Directory.Exists(Path.Combine(sharedRoot, "themes")))
            throw new DirectoryNotFoundException($"shared/themes 폴더를 찾을 수 없어요: {sharedRoot}");
        SharedRoot = sharedRoot;
    }

    /// <summary>앱 실행 폴더의 shared/ → 없으면 상위 폴더를 거슬러 올라가며 찾는다(개발 중 실행 대비).</summary>
    public static string LocateSharedRoot(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "shared");
            if (Directory.Exists(Path.Combine(candidate, "themes")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("shared/themes 폴더를 찾을 수 없어요.");
    }

    public IReadOnlyList<string> ListThemeSlugs() =>
        Directory.GetDirectories(Path.Combine(SharedRoot, "themes"))
            .Where(d => File.Exists(Path.Combine(d, "theme.yaml")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    public ThemePack Load(string slug)
    {
        var dir = Path.Combine(SharedRoot, "themes", slug);
        var file = Path.Combine(dir, "theme.yaml");
        if (!File.Exists(file))
            throw new FileNotFoundException($"테마 '{slug}'를 찾을 수 없어요.", file);

        var theme = Yaml.Deserialize<ThemePack>(File.ReadAllText(file)) ?? new ThemePack();
        if (string.IsNullOrEmpty(theme.Slug)) theme.Slug = slug;

        var curriculumDir = Path.Combine(dir, "curriculum");
        if (Directory.Exists(curriculumDir))
        {
            foreach (var path in Directory.GetFiles(curriculumDir, "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
            {
                var track = Yaml.Deserialize<Track>(File.ReadAllText(path)) ?? new Track();
                track.Key = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(track.Name)) track.Name = track.Key;
                theme.Curriculum[track.Key] = track;
            }
        }
        return theme;
    }

    /// <summary>shared/prompts/base.md — 모든 테마 공통 규칙(맨 앞 HTML 주석은 제거).</summary>
    public string LoadBasePrompt()
    {
        var text = File.ReadAllText(Path.Combine(SharedRoot, "prompts", "base.md"));
        return System.Text.RegularExpressions.Regex.Replace(text, @"^\s*<!--.*?-->\s*", "",
            System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
    }
}
