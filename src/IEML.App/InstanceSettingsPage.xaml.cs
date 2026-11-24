using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// 伺服器清單的一列（含即時狀態與是否為自動加入）。
public class ServerRow : System.ComponentModel.INotifyPropertyChanged
{
    string _name = "", _status = "尚未查詢"; bool _auto;
    public string Name { get => _name; set { _name = value; Raise(nameof(Name)); } }
    public string Address { get; set; } = "";
    public string StatusText { get => _status; set { _status = value; Raise(nameof(StatusText)); } }
    public bool IsAutoJoin { get => _auto; set { _auto = value; Raise(nameof(IsAutoJoin)); Raise(nameof(Star)); Raise(nameof(AutoText)); } }
    public string Star => _auto ? "★ " : "";
    public string AutoText => _auto ? "取消自動加入" : "設為自動加入";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new(n));
}

/// 實例設置頁（取代彈出視窗）：基本／模組／光影／資源包／快捷卡片。嵌在主畫面的覆蓋頁裡。
public partial class InstanceSettingsPage : UserControl
{
    public InstanceConfig Config { get; }
    public IReadOnlyList<Account> Accounts { get; }
    public ObservableCollection<QuickLaunchCard> Cards { get; }
    public ObservableCollection<ServerRow> Servers { get; }
    readonly ServerPingService _ping = new();
    readonly IInstanceService _instances;
    readonly Action<bool> _close;
    string? _backup;
    bool _saved;
    /// 按「啟動並加入」時設定；關閉頁面後由主畫面讀取並啟動。
    public string? LaunchServer { get; private set; }

    public InstanceSettingsPage(InstanceConfig cfg, IReadOnlyList<Account> accounts, IInstanceService instances, Action<bool> close)
    {
        Config = cfg; Accounts = accounts; _instances = instances; _close = close;
        _backup = System.Text.Json.JsonSerializer.Serialize(cfg);
        Cards = new(cfg.QuickCards.Select(c => new QuickLaunchCard { Id = c.Id, AccountId = c.AccountId, X = c.X, Y = c.Y }));
        Servers = new(cfg.Servers.Select(s => new ServerRow
        {
            Name = string.IsNullOrWhiteSpace(s.Name) ? s.Address : s.Name, Address = s.Address,
            IsAutoJoin = string.Equals(cfg.AutoJoinServer, s.Address, StringComparison.OrdinalIgnoreCase)
        }));
        InitializeComponent();
        DataContext = this;
        _origMc = cfg.McVersion;
        LoaderBox.ItemsSource = Enum.GetValues<LoaderType>();
        LoaderBox.SelectedItem = cfg.Loader;   // 會觸發 LoaderBox_Changed → 載入版本清單
    }

    const string AutoText = "自動（最新穩定版）";
    readonly string _origMc;
    CancellationTokenSource? _cts;
    bool _initialLoad = true;

    void LoaderBox_Changed(object s, SelectionChangedEventArgs e)
    {
        if (LoaderVerBox is null) return;
        _ = RefreshLoaderVersionsAsync(resetText: !_initialLoad);
        _initialLoad = false;
    }
    void McBox_LostFocus(object s, RoutedEventArgs e) => _ = RefreshLoaderVersionsAsync(resetText: false);
    void RefreshLoaderVer_Click(object s, RoutedEventArgs e) => _ = RefreshLoaderVersionsAsync(resetText: false);

    LoaderType SelectedLoader => LoaderBox.SelectedItem is LoaderType l ? l : Config.Loader;

    async Task RefreshLoaderVersionsAsync(bool resetText)
    {
        _cts?.Cancel(); _cts = new(); var ct = _cts.Token;
        var loader = SelectedLoader;
        var mc = McBox.Text.Trim();
        // 目前要保留的版本：第一次開窗＝實例存的版本；之後＝使用者目前選的
        var keep = resetText ? AutoText
            : string.IsNullOrWhiteSpace(LoaderVerBox.Text) ? (Config.LoaderVersion ?? AutoText) : LoaderVerBox.Text.Trim();
        if (loader == LoaderType.Vanilla)
        {
            LoaderVerBox.ItemsSource = null; LoaderVerBox.Text = ""; LoaderVerBox.IsEnabled = false;
            LoaderHint.Text = "原版沒有載入器。"; return;
        }
        LoaderVerBox.IsEnabled = true;
        LoaderHint.Text = "載入版本清單中…";
        try
        {
            var list = await LoaderVersionService.ListAsync(loader, mc, ct);
            if (ct.IsCancellationRequested) return;
            var items = new List<string> { AutoText }; items.AddRange(list);
            LoaderVerBox.ItemsSource = items;
            // 原本選的版本不屬於這個 MC 版本／載入器 → 退回自動
            LoaderVerBox.Text = keep == AutoText || list.Contains(keep) ? keep : AutoText;
            LoaderHint.Text = list.Count == 0
                ? $"找不到 {mc} 可用的 {loader} 版本（可能不支援這個 Minecraft 版本）。"
                : $"共 {list.Count} 個版本（穩定版在前）。可直接輸入版本號。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            LoaderVerBox.ItemsSource = new[] { AutoText };
            LoaderVerBox.Text = keep;                       // 離線時保留原值，還是可以手打
            LoaderHint.Text = "無法取得版本清單：" + ex.Message;
        }
    }

    async void PickJava_Click(object s, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow mw) return;
        JavaPickerPanel? w = null;
        if (!await mw.ShowModal("選擇 Java", close => w = new JavaPickerPanel(Config.McVersion, close), 620) || w is null) return;
        Config.JavaPath = w.SelectedPath;
        JavaBox.Text = w.SelectedPath ?? "";
    }

    void AddCard_Click(object s, RoutedEventArgs e) =>
        Cards.Add(new QuickLaunchCard { AccountId = Accounts.FirstOrDefault()?.Id ?? Guid.Empty });

    void RemoveCard_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is QuickLaunchCard c) Cards.Remove(c);
    }

    async void Save_Click(object s, RoutedEventArgs e) => await SaveAsync();

    async Task SaveAsync()
    {
        McBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();   // 按 Enter 儲存時，確保 MC 版本已寫回
        var newLoader = SelectedLoader;
        var verText = LoaderVerBox.Text?.Trim();
        string? newVer = newLoader == LoaderType.Vanilla || string.IsNullOrEmpty(verText) || verText == AutoText ? null : verText;

        // 換載入器種類：已安裝的模組通常不能通用，先提醒
        if (newLoader != Config.Loader && Config.Loader != LoaderType.Vanilla)
        {
            var mods = System.IO.Path.Combine(InstanceRoots.DirFor(Config.Id), "mods");
            if (System.IO.Directory.Exists(mods) && System.IO.Directory.EnumerateFiles(mods, "*.jar").Any() &&
                !await Dialogs.ConfirmAsync("更換載入器", $"從 {Config.Loader} 換成 {newLoader} 後，已安裝的模組多半無法使用，要繼續嗎？\n（模組檔案不會被刪除）", "繼續")) return;
        }
        // MC 版本改了、但選的載入器版本不在新清單內 → 清成自動（避免用到舊 MC 的載入器版本）
        if (Config.McVersion != _origMc && newVer != null && LoaderVerBox.Items.Count > 0 && !LoaderVerBox.Items.Contains(newVer)) newVer = null;

        Config.Loader = newLoader;
        Config.LoaderVersion = newVer;
        Config.QuickCards = Cards.Where(c => c.AccountId != Guid.Empty).ToList();
        Config.Servers = Servers.Select(r => new InstanceServer { Name = r.Name, Address = r.Address }).ToList();
        Config.AutoJoinServer = Servers.FirstOrDefault(r => r.IsAutoJoin)?.Address;
        _backup = null;               // 已保存，不用還原
        _saved = true;
        _close(true);
    }

    void Cancel_Click(object s, RoutedEventArgs e) { Discard(); _close(false); }

    /// 放棄修改：文字框是雙向綁定，直接改了 Config，所以要從開頁時的備份還原。
    public void Discard()
    {
        if (_backup is null) return;
        var o = System.Text.Json.JsonSerializer.Deserialize<InstanceConfig>(_backup);
        if (o is null) return;
        foreach (var p in typeof(InstanceConfig).GetProperties())
            if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0) p.SetValue(Config, p.GetValue(o));
        _backup = null;
    }

    // ---------- 伺服器清單 ----------
    void AddServer_Click(object s, RoutedEventArgs e)
    {
        var addr = SrvAddrBox.Text.Trim();
        if (addr.Length == 0) { SrvHint.Text = "請先輸入伺服器位址。"; return; }
        if (Servers.Any(r => string.Equals(r.Address, addr, StringComparison.OrdinalIgnoreCase))) { SrvHint.Text = "這個位址已經在清單裡。"; return; }
        var row = new ServerRow { Name = string.IsNullOrWhiteSpace(SrvNameBox.Text) ? addr : SrvNameBox.Text.Trim(), Address = addr };
        Servers.Add(row);
        SrvNameBox.Clear(); SrvAddrBox.Clear();
        _ = PingRowAsync(row);
    }

    void DelServer_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is ServerRow r) Servers.Remove(r);
    }

    void ToggleAuto_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not ServerRow r) return;
        bool on = !r.IsAutoJoin;
        foreach (var x in Servers) x.IsAutoJoin = false;     // 同時只能有一個自動加入
        r.IsAutoJoin = on;
    }

    async void JoinServer_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not ServerRow r) return;
        LaunchServer = r.Address;
        _saved = false;
        await SaveAsync();                      // 跟「保存」同一套流程（含換載入器的確認）
        if (!_saved) LaunchServer = null;       // 使用者在確認框按了否 → 不啟動
    }

    void CopyServer_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is ServerRow r) { try { Clipboard.SetText(r.Address); SrvHint.Text = "已複製：" + r.Address; } catch { } }
    }

    void PingAll_Click(object s, RoutedEventArgs e) => _ = Task.WhenAll(Servers.ToList().Select(PingRowAsync));

    async Task PingRowAsync(ServerRow r)
    {
        r.StatusText = "查詢中…";
        try
        {
            var st = await _ping.PingAsync(r.Address);
            r.StatusText = st.Online
                ? $"● 在線 {st.Players}/{st.MaxPlayers}　{st.LatencyMs}ms　{st.Version}　{st.Motd.Replace('\n', ' ')}"
                : "○ 離線或無法連線";
        }
        catch (Exception ex) { r.StatusText = "查詢失敗：" + ex.Message; }
    }

    bool _pinged;

    // ---------- 內容分頁（模組／光影／資源包）：第一次選到才建立 ----------
    void Tabs_Changed(object s, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs) return;                       // 內層 ComboBox／TabControl 的事件不算
        if (Tabs.SelectedItem is not TabItem { Tag: string tag }) return;
        if (tag == "servers" && !_pinged) { _pinged = true; PingAll_Click(s, e); return; }
        if (!Enum.TryParse<ContentKind>(tag, out var kind)) return;
        var host = kind switch { ContentKind.Mod => ModHost, ContentKind.Shader => ShaderHost, _ => PackHost };
        if (host.Content is not null) return;
        var view = new ContentView(Config, new ModrinthService(_instances));
        view.LockKind(kind);
        view.ManageOnly();      // 實例設置只管理已安裝的內容；要下載請到下載頁
        host.Content = view;
    }
}
