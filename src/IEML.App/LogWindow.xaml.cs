using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using IEML.Core.Services;
namespace IEML.App;

public partial class LogWindow : Window
{
    const int MaxChars = 600_000;
    readonly ConcurrentQueue<string> _queue = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    int _lines;

    public LogWindow()
    {
        InitializeComponent();
        var snap = GameLog.Snapshot();
        LogBox.Text = string.Join(Environment.NewLine, snap) + (snap.Count > 0 ? Environment.NewLine : "");
        _lines = snap.Count;
        UpdateCount();
        GameLog.LineAdded += OnLine;
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
        Closed += (_, _) => { GameLog.LineAdded -= OnLine; _timer.Stop(); };
        Loaded += (_, _) => LogBox.ScrollToEnd();
    }

    // 背景執行緒進來：先排隊，由計時器批次寫入畫面，避免每行都刷新 UI
    void OnLine(string line) => _queue.Enqueue(line);

    void Flush()
    {
        if (_queue.IsEmpty) return;
        var sb = new System.Text.StringBuilder();
        while (_queue.TryDequeue(out var l)) { sb.AppendLine(l); _lines++; }
        LogBox.AppendText(sb.ToString());
        if (LogBox.Text.Length > MaxChars) LogBox.Text = LogBox.Text[^(MaxChars / 2)..];
        if (AutoScroll.IsChecked == true) LogBox.ScrollToEnd();
        UpdateCount();
    }

    void UpdateCount() => CountText.Text = $"{_lines} 行";

    void Clear_Click(object s, RoutedEventArgs e)
    {
        GameLog.Clear();
        while (_queue.TryDequeue(out _)) { }
        LogBox.Clear(); _lines = 0; UpdateCount();
    }

    void Copy_Click(object s, RoutedEventArgs e)
    {
        try { Clipboard.SetText(LogBox.Text); CountText.Text = "已複製"; }
        catch (Exception ex) { CountText.Text = "複製失敗：" + ex.Message; }
    }
}
