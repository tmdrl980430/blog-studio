using System.Collections.ObjectModel;
using BlogStudio.Core.Storage;
using BlogStudio.Core.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogStudio.Presentation;

public sealed record TrackOption(string Key, string Name);

/// <summary>목록 한 줄 (같은 주제 글이 두 개여도 따로 움직이게 참조 타입).</summary>
public sealed class TopicRow
{
    public TopicRow(string text) => Text = text;
    public string Text { get; }
}

public sealed partial class DaySlot : ObservableObject
{
    public DaySlot(string key, string label, TrackOption selected)
    {
        Key = key;
        Label = label;
        _selected = selected;
    }

    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private TrackOption _selected;
}

/// <summary>
/// 주제 관리: 트랙별 "앞으로 쓸 주제" 추가·순서 바꾸기·빼기, 요일별 기술 트랙 바꾸기.
/// 이미 쓴 주제(진도)는 건드리지 않는다.
/// </summary>
public sealed partial class TopicManagerViewModel : ObservableObject
{
    private readonly LocalStore _store;
    private readonly IReadOnlyList<ThemePack> _themes;
    private readonly HashSet<string> _dirty = new();
    private readonly Dictionary<string, List<TopicRow>> _queues = new();

    public static readonly TrackOption Rest = new("rest", "쉬는 날");
    private static readonly (string Key, string Label)[] Days =
        { ("mon", "월"), ("tue", "화"), ("wed", "수"), ("thu", "목"), ("fri", "금"), ("sat", "토"), ("sun", "일") };

    public TopicManagerViewModel(LocalStore store, IReadOnlyList<ThemePack> themes)
    {
        _store = store;
        _themes = themes;
        foreach (var theme in themes)
            foreach (var key in theme.Tracks.Where(theme.Curriculum.ContainsKey))
                Tracks.Add(new TrackOption(key, theme.Curriculum[key].Name));

        // 요일: 첫 테마(기술)의 트랙만
        var tech = themes[0];
        TechTracks = tech.Tracks.Where(tech.Curriculum.ContainsKey)
            .Select(k => new TrackOption(k, tech.Curriculum[k].Name)).Append(Rest).ToList();
        var settings = store.LoadSettings();
        var schedule = settings.Schedule is { Count: > 0 } ? settings.Schedule : tech.DefaultSchedule;
        foreach (var (key, label) in Days)
        {
            var track = schedule.GetValueOrDefault(key, "rest");
            Schedule.Add(new DaySlot(key, label, TechTracks.FirstOrDefault(t => t.Key == track) ?? Rest));
        }

        _selectedTrack = Tracks.FirstOrDefault();
        LoadRows();
    }

    public ObservableCollection<TrackOption> Tracks { get; } = new();
    public IReadOnlyList<TrackOption> TechTracks { get; }
    public ObservableCollection<DaySlot> Schedule { get; } = new();
    public ObservableCollection<TopicRow> Upcoming { get; } = new();

    [ObservableProperty] private TrackOption? _selectedTrack;
    [ObservableProperty] private string _newTopic = "";
    [ObservableProperty] private string _doneText = "";

    public event EventHandler? Saved;

    partial void OnSelectedTrackChanged(TrackOption? value) => LoadRows();

    /// <summary>테마 파일의 원래 주제 (생성자에는 사용자 순서를 적용하지 않은 테마를 넘긴다).</summary>
    private IReadOnlyList<string> Original(string key) =>
        _themes.Select(t => t.Curriculum.GetValueOrDefault(key)).FirstOrDefault(t => t is not null)?.Topics ?? new List<string>();

    private void LoadRows()
    {
        Upcoming.Clear();
        if (SelectedTrack is not { } track) return;
        var done = _store.NextIndex(track.Key);
        if (!_queues.TryGetValue(track.Key, out var rows))
        {
            var upcoming = _store.UpcomingQueue(track.Key) ?? Original(track.Key).Skip(done).ToList();
            _queues[track.Key] = rows = upcoming.Where(t => t.Length > 0).Select(t => new TopicRow(t)).ToList();
        }
        foreach (var r in rows) Upcoming.Add(r);
        DoneText = done == 0 ? "아직 쓴 글 없음 — 아래 순서대로 써요" : $"이미 쓴 주제 {done}개는 그대로 두고, 아래가 다음에 쓸 순서예요";
    }

    private void Sync()
    {
        if (SelectedTrack is null) return;
        _queues[SelectedTrack.Key] = Upcoming.ToList();
        _dirty.Add(SelectedTrack.Key);
        _resets.Remove(SelectedTrack.Key);
    }

    [RelayCommand]
    private void AddNext() => Add(first: true);

    [RelayCommand]
    private void AddLast() => Add(first: false);

    private void Add(bool first)
    {
        var text = NewTopic.Trim();
        if (text.Length == 0 || SelectedTrack is null) return;
        if (first) Upcoming.Insert(0, new TopicRow(text)); else Upcoming.Add(new TopicRow(text));
        NewTopic = "";
        Sync();
    }

    [RelayCommand]
    private void MoveUp(TopicRow? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(TopicRow? row) => Move(row, +1);

    private void Move(TopicRow? row, int delta)
    {
        if (row is null) return;
        var i = Upcoming.IndexOf(row);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Upcoming.Count) return;
        Upcoming.Move(i, j);
        Sync();
    }

    [RelayCommand]
    private void Remove(TopicRow? row)
    {
        if (row is not null && Upcoming.Remove(row)) Sync();
    }

    /// <summary>이 트랙을 원래 커리큘럼 순서로 되돌림 (저장을 눌러야 반영).</summary>
    [RelayCommand]
    private void ResetTrack()
    {
        if (SelectedTrack is null) return;
        var done = _store.NextIndex(SelectedTrack.Key);
        _queues[SelectedTrack.Key] = Original(SelectedTrack.Key).Skip(done).Select(t => new TopicRow(t)).ToList();
        _resets.Add(SelectedTrack.Key);
        _dirty.Remove(SelectedTrack.Key);
        LoadRows();
    }

    private readonly HashSet<string> _resets = new();

    [RelayCommand]
    private void Save()
    {
        foreach (var key in _resets.Except(_dirty))
            _store.SaveTopicQueue(key, null);
        foreach (var key in _dirty)
            _store.SaveTopicQueue(key, _queues[key].Select(r => r.Text).ToList());
        var settings = _store.LoadSettings();
        settings.Schedule = Schedule.ToDictionary(d => d.Key, d => d.Selected.Key);
        _store.SaveSettings(settings);
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
