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

    public StatsWindow(BlogStats stats)
    {
        InitializeComponent();
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
