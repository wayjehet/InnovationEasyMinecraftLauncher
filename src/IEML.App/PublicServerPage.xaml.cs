using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IEML.Core.Contracts;
using IEML.Core.Services;
namespace IEML.App;

public partial class PublicServerPage : UserControl
{
    readonly PublicServerService _svc = new();
    readonly ServerPingService _ping = new();
    readonly List<PublicServer> _items = new();
    readonly Dictionary<string, ServerStatus> _live = new(StringComparer.OrdinalIgnoreCase);
    string _keyword = "";
    int _page = 1;

    public ObservableCollection<string> Lines { get; } = new();

    public PublicServerPage()
    {
        InitializeComponent(); DataContext = this;
        Loaded += async (_, _) => await LoadAsync();
    }

    static string Fmt(PublicServer s, ServerStatus? live)
    {
        var line = $"{s.Name}  —  {s.Address}";
        if (s.Version is not null) line += $"  [{s.Version}]";
        if (live is not null)
            line += live.Online ? $"  ● 即時 {live.Players}/{live.MaxPlayers}  {live.LatencyMs}ms" : "  ○ 即時：離線或無法連線";
        else if (s.Players is not null)
            line += $"  ({s.Players}{(s.MaxPlayers is null ? "" : "/" + s.MaxPlayers)} 人)";
        return line;
    }

    void Rebuild()
    {
        var sel = List.SelectedIndex;
        Lines.Clear();
        foreach (var s in _items) Lines.Add(Fmt(s, _live.GetValueOrDefault(s.Address)));
        if (sel >= 0 && sel < Lines.Count) List.SelectedIndex = sel;
    }

    async Task LoadAsync()
    {
        SearchBtn.IsEnabled = PrevBtn.IsEnabled = NextBtn.IsEnabled = false;
        StatusText.Text = "查詢中…";
        try
        {
            var r = await _svc.SearchAsync(_keyword, _page);
            _items.Clear(); _items.AddRange(r); _live.Clear();
            Rebuild();
            PageText.Text = $"第 {_page} 頁";
            StatusText.Text = r.Count == 0 ? "沒有符合的伺服器（或回應格式與預期不同）。" : $"共 {r.Count} 筆。資料為站方快取，可用「即時查詢」確認。";
            NextBtn.IsEnabled = r.Count >= 25 && _page < 100; // 每頁上限 25 筆
        }
        catch (Exception ex) { StatusText.Text = "錯誤：" + ex.Message; }
        finally { SearchBtn.IsEnabled = true; PrevBtn.IsEnabled = _page > 1; }
    }

    async void Search_Click(object s, RoutedEventArgs e) { _keyword = KeywordBox.Text; _page = 1; await LoadAsync(); }
    void KeywordBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Search_Click(s, e); }
    async void Prev_Click(object s, RoutedEventArgs e) { if (_page > 1) { _page--; await LoadAsync(); } }
    async void Next_Click(object s, RoutedEventArgs e) { _page++; await LoadAsync(); }

    PublicServer? Selected => List.SelectedIndex >= 0 && List.SelectedIndex < _items.Count ? _items[List.SelectedIndex] : null;

    async void Live_Click(object s, RoutedEventArgs e)
    {
        var sv = Selected;
        if (sv is null) { StatusText.Text = "請先選一個伺服器。"; return; }
        StatusText.Text = $"即時查詢 {sv.Address}…";
        try { _live[sv.Address] = await _ping.PingAsync(sv.Address); Rebuild(); StatusText.Text = "完成。"; }
        catch (Exception ex) { StatusText.Text = "錯誤：" + ex.Message; }
    }

    async void Fav_Click(object s, RoutedEventArgs e)
    {
        var sv = Selected;
        if (sv is null) { StatusText.Text = "請先選一個伺服器。"; return; }
        try { await _ping.SaveFavoriteAsync(sv.Address); StatusText.Text = $"已加入收藏：{sv.Address}"; }
        catch (Exception ex) { StatusText.Text = "錯誤：" + ex.Message; }
    }

    void Copy_Click(object s, RoutedEventArgs e)
    {
        var sv = Selected;
        if (sv is null) { StatusText.Text = "請先選一個伺服器。"; return; }
        try { Clipboard.SetText(sv.Address); StatusText.Text = "已複製位址"; }
        catch (Exception ex) { StatusText.Text = "複製失敗：" + ex.Message; }
    }
}
