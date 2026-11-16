using System.Text.RegularExpressions;

namespace BlogStudio.Core.Tests;

/// <summary>
/// 창(Window)마다 리소스 범위가 달라서, 한 창에만 정의한 키를 다른 창에서 {StaticResource}로 쓰면
/// 창을 열 때 "StaticResourceExtension 값 제공에서 예외" 오류가 난다 (이슈 #24, 설정 창의 BoolToVis).
/// WPF 없이 XAML 글자만 읽어 모든 참조 키가 App.xaml 또는 그 파일 안에 정의돼 있는지 확인한다.
/// </summary>
public class XamlResourceTests
{
    private static readonly Regex KeyDef = new(@"x:Key\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex KeyRef = new(@"\{(?:StaticResource|DynamicResource)\s+(?:ResourceKey\s*=\s*)?([A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);

    private static string WindowsAppDir()
    {
        var repoRoot = Directory.GetParent(Themes.ThemeRepository.LocateSharedRoot())!.FullName;
        return Path.Combine(repoRoot, "apps", "desktop", "BlogStudio.Windows");
    }

    private static HashSet<string> Defined(string xaml) =>
        KeyDef.Matches(xaml).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> Referenced(string xaml) =>
        KeyRef.Matches(xaml).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal);

    [Fact]
    public void Every_static_resource_key_is_defined_in_app_or_same_file()
    {
        var dir = WindowsAppDir();
        var appXaml = File.ReadAllText(Path.Combine(dir, "App.xaml"));
        var appKeys = Defined(appXaml);

        var files = Directory.GetFiles(dir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count >= 2, "XAML 파일을 찾지 못했어요.");

        var missing = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var local = Defined(text);
            foreach (var key in Referenced(text))
            {
                if (!appKeys.Contains(key) && !local.Contains(key))
                    missing.Add($"{Path.GetRelativePath(dir, file)}: {key}");
            }
        }

        Assert.True(missing.Count == 0,
            "App.xaml에도, 그 창에도 정의되지 않은 리소스 키: " + string.Join(", ", missing));
    }

    [Fact]
    public void Shared_converter_lives_in_app_resources()
    {
        var dir = WindowsAppDir();
        Assert.Contains("BoolToVis", Defined(File.ReadAllText(Path.Combine(dir, "App.xaml"))));
        // 설정 창은 MainWindow 리소스를 볼 수 없으므로 BoolToVis를 App.xaml에서 가져와야 한다.
        Assert.Contains("BoolToVis", Referenced(File.ReadAllText(Path.Combine(dir, "SettingsWindow.xaml"))));
    }

    [Fact]
    public void Key_scanner_catches_window_only_keys()
    {
        var app = Defined(@"<SolidColorBrush x:Key=""Ink"" />");
        var window = @"<Window><Window.Resources><X x:Key=""Local"" /></Window.Resources>
            <A Foreground=""{StaticResource Ink}"" B=""{StaticResource Local}"" C=""{StaticResource OtherWindowOnly}"" /></Window>";
        var local = Defined(window);
        var missing = Referenced(window).Where(k => !app.Contains(k) && !local.Contains(k)).ToList();
        Assert.Equal(new[] { "OtherWindowOnly" }, missing);
    }
}
