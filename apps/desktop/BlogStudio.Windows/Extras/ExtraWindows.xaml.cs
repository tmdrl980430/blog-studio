using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlogStudio.Core.Stats;
using BlogStudio.Presentation;

namespace BlogStudio.Windows.Extras;

public partial class TopicManagerWindow : Window
{
    public TopicManagerWindow(TopicManagerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Saved += (_, _) => DialogResult = true;
    }
}

public partial class StatsWindow : Window
{
    private sealed record DayBar(double Height, Brush Brush, string Tip);
    private sealed record CategoryBar(string Name, double BarWidth, string Label);
    private sealed record AutoLogRow(string Time, string Badge, Brush Brush, string Title, string Detail);

    private readonly MainViewModel? _vm;

    public StatsWindow(BlogStats stats, MainViewModel? vm = null)
    {
        InitializeComponent();
        _vm = vm;
        if (vm is null) AnalystPanel.Visibility = Visibility.Collapsed;
        else AnalystPanel.DataContext = vm;
        Streak.Text = $"{stats.StreakDays}일";
        Best.Text = $"최고 {stats.BestStreak}일";
        Recent.Text = $"{stats.ThisWeek} · {stats.ThisMonth}";
        Total.Text = $"{stats.Total}편";
        Unpublished.Text = $"{stats.Unpublished}편";
        Naver.Text = $"{stats.PublishedNaver}편";
        Tistory.Text = $"{stats.PublishedTistory}편";

        var max = Math.Max(1, stats.Last30Days.Max(d => d.Count));
        var accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x5B, 0xEA));
        var empty = new SolidColorBrush(Color.FromRgb(0xE3, 0xE6, 0xEB));
        Days.ItemsSource = stats.Last30Days.Select(d => new DayBar(
            d.Count == 0 ? 4 : 12 + 74.0 * d.Count / max, d.Count == 0 ? empty : accent, $"{d.Date:M/d} · {d.Count}편")).ToList();
        FirstDay.Text = $"{stats.Last30Days[0].Date:M/d}";
        LastDay.Text = "오늘";

        var top = Math.Max(1, stats.ByCategory.Select(c => c.Count).DefaultIfEmpty(0).Max());
        Categories.ItemsSource = stats.ByCategory.Select(c => new CategoryBar(
            c.Name, Math.Max(4, 320.0 * c.Count / top), $"{c.Count}편 · 올림 {c.Published}")).ToList();
        ShowAutoLog(vm?.LoadAutoLog() ?? Array.Empty<AutoLogEntry>(), vm is not null);
        Closed += (_, _) =>
        {
            if (_vm?.AskAnalystCancelCommand.CanExecute(null) == true) _vm.AskAnalystCancelCommand.Execute(null);
        };
    }

    private void ShowAutoLog(IReadOnlyList<AutoLogEntry> entries, bool available)
    {
        if (!available) { AutoLogPanel.Visibility = Visibility.Collapsed; return; }
        AutoLogEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AutoLogList.ItemsSource = entries.Select(e =>
        {
            var (badge, color) = e.Outcome switch
            {
                AutoLogOutcome.Published => ("발행", Color.FromRgb(0x03, 0xA9, 0x4D)),
                AutoLogOutcome.Partial => ("일부", Color.FromRgb(0xB4, 0x53, 0x09)),
                AutoLogOutcome.Held => ("보류", Color.FromRgb(0xB4, 0x53, 0x09)),
                AutoLogOutcome.Off => ("작성만", Color.FromRgb(0x6B, 0x72, 0x80)),
                AutoLogOutcome.Failed => ("실패", Color.FromRgb(0xDC, 0x26, 0x26)),
                AutoLogOutcome.Skipped => ("건너뜀", Color.FromRgb(0xB4, 0x53, 0x09)),
                _ => ("기록", Color.FromRgb(0x6B, 0x72, 0x80)),
            };
            var detail = e.Outcome == AutoLogOutcome.Other ? "" : $"편집자 {e.Editor} · {e.Result}";
            return new AutoLogRow(e.TimeLabel, badge, new SolidColorBrush(color), e.Title, detail);
        }).ToList();
    }

    private void OpenAutoLog_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm?.AutoLogPath;
        if (path is null || !File.Exists(path))
        {
            MessageBox.Show(this, "아직 auto-log.txt가 없어요.", "자동 발행 기록");
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "파일을 열지 못했어요: " + ex.Message, "자동 발행 기록"); }
    }

    /// <summary>분석가 추천 줄 → 창을 닫고 메인 화면에서 그 트랙으로 기획자 주제 받기.</summary>
    private void AnalystPick_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not FrameworkElement { Tag: BlogStudio.Core.Staff.TrackPriority pick }) return;
        Close();
        _vm.PlanAnalystPickCommand.Execute(pick);
    }
}

public partial class InstaWindow : Window
{
    private readonly string _folder;

    public InstaWindow(IReadOnlyList<string> files, string caption)
    {
        InitializeComponent();
        _folder = Path.GetDirectoryName(files[0])!;
        Caption.Text = caption;
        Cards.ItemsSource = files.Select(f =>
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;    // 다시 만들 때 파일이 잠기지 않게
            image.DecodePixelWidth = 540;
            image.UriSource = new Uri(f);
            image.EndInit();
            image.Freeze();
            return image;
        }).ToList();
    }

    private void CopyCaption_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(Caption.Text);

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true });
}

/// <summary>shared/guides 문서를 테마 값으로 채워 보여 주고, CSS·HTML 코드는 버튼으로 복사한다.</summary>
public partial class GuideWindow : Window
{
    private bool _webReady;

    public GuideWindow(IReadOnlyList<BlogStudio.Core.Themes.BlogGuide> guides, string themeName)
    {
        InitializeComponent();
        SubTitle.Text = $"'{themeName}' 테마 기준으로 카테고리·색을 채웠어요. 블로그를 처음 만들거나 꾸밀 때 위에서부터 따라 하세요.";
        View.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlogStudio", "WebView2"),
        };
        Guides.ItemsSource = guides;
        Loaded += async (_, _) =>
        {
            try
            {
                await View.EnsureCoreWebView2Async();
                _webReady = true;
            }
            catch (Exception)
            {
                View.Visibility = Visibility.Collapsed;
                Fallback.Visibility = Visibility.Visible;
            }
            if (guides.Count > 0) Guides.SelectedIndex = 0;
            else ShowGuide(null);
        };
    }

    private void Guides_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        ShowGuide(Guides.SelectedItem as BlogStudio.Core.Themes.BlogGuide);

    private void ShowGuide(BlogStudio.Core.Themes.BlogGuide? guide)
    {
        CodeBlocks.ItemsSource = guide?.CodeBlocks;
        var hasCode = guide is { CodeBlocks.Count: > 0 };
        CodeHeader.Visibility = hasCode ? Visibility.Visible : Visibility.Collapsed;
        CopyStatus.Text = "";
        if (_webReady)
            View.NavigateToString(guide?.ToHtmlPage()
                ?? BlogStudio.Core.Rendering.BlogHtmlRenderer.EmptyPage("가이드 문서를 찾지 못했어요 (shared/guides)"));
        else
            Fallback.Text = guide?.Markdown ?? "가이드 문서를 찾지 못했어요 (shared/guides)";
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BlogStudio.Core.Themes.GuideCodeBlock block) return;
        try
        {
            Clipboard.SetText(block.Code);
            CopyStatus.Text = $"{block.Language.ToUpperInvariant()} 코드를 복사했어요";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopyStatus.Text = "클립보드를 쓸 수 없어요. 잠시 뒤 다시 눌러 주세요";
        }
    }
}
