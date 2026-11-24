using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// 下載中心「遊戲」分頁：兩步驟建立實例。
/// 步驟 1 選版本（搜尋＋正式版／快照／舊版）；步驟 2 取名、選載入器（只列出這個版本真的有的）與載入器版本，
/// 按「建立並安裝」後真的下載遊戲檔案與載入器（有進度、可取消），第一次啟動就不用再等。
public partial class GameInstallView : UserControl
{
    static readonly LoaderType[] LoaderOrder = { LoaderType.Vanilla, LoaderType.Fabric, LoaderType.Quilt, LoaderType.NeoForge, LoaderType.Forge };
    static readonly LoaderType[] Remote = { LoaderType.Fabric, LoaderType.Quilt, LoaderType.NeoForge, LoaderType.Forge };

    IInstanceService? _instances;
    GameInstaller? _installer;
    List<GameVersionInfo> _all = new();
    string _group = "release";
    GameVersionInfo? _picked;
    LoaderType _loader = LoaderType.Vanilla;
    readonly Dictionary<LoaderType, IReadOnlyList<string>> _loaderVersions = new();
    readonly Dictionary<LoaderType, Button> _chips = new();
    bool _settingName, _nameTouched, _busy;
    int _token;
    CancellationTokenSource? _cts;

    /// 實例建立後（磁碟上已有設定檔）觸發，宿主可據此重新整理實例清單。
    public event Action? InstanceCreated;

    public GameInstallView()
    {
        InitializeComponent();
        foreach (var l in LoaderOrder)
        {
            var b = new Button { Content = LoaderText(l), Tag = l, Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += async (_, _) => await SelectLoaderAsync(l);
            _chips[l] = b;
            LoaderChips.Children.Add(b);
        }
        Loaded += async (_, _) => { if (_all.Count == 0) await LoadVersionsAsync(); };
    }

    public void Initialize(IInstanceService instances)
    {
        _instances = instances;
        _installer = new GameInstaller(instances);
    }

    static string LoaderText(LoaderType l) => l == LoaderType.Vanilla ? "原版" : l.ToString();

    // ---------- 步驟 1：版本清單 ----------
    async Task LoadVersionsAsync()
    {
        ListStatus.Text = "載入版本清單中…";
        try
        {
            _all = (await VersionService.ListDetailedAsync()).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { ListStatus.Text = "載入版本清單失敗：" + ex.Message; }
    }

    void ApplyFilter()
    {
        var text = SearchBox.Text.Trim();
        var list = _all.Where(v => v.Group == _group && (text.Length == 0 || v.Id.Contains(text, StringComparison.OrdinalIgnoreCase))).ToList();
        VersionList.ItemsSource = list;
        ChipRelease.Opacity = _group == "release" ? 1 : .55;
        ChipSnapshot.Opacity = _group == "snapshot" ? 1 : .55;
        ChipApril.Opacity = _group == "april" ? 1 : .55;
        ChipOld.Opacity = _group == "old" ? 1 : .55;
        var offline = _all.Count > 0 && _all.All(v => v.Released is null);
        ListStatus.Text = list.Count == 0 ? (_group == "april" && _all.Count > 0 && _all.All(v => v.Released is null) ? "離線時沒有愚人節版本的清單，連上網路後再試。" : "沒有符合的版本（試試換類型或清除搜尋）。")
            : $"{list.Count} 個版本，點一個繼續。"
              + (_group == "april" ? "愚人節版本是玩笑版本，通常不能搭配載入器或模組。" : "")
              + (offline ? "（目前像是離線，只列出內建的常用版本。）" : "");
    }

    void Search_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }

    void Chip_Click(object sender, RoutedEventArgs e)
    {
        _group = (sender as FrameworkElement)?.Tag as string ?? "release";
        ApplyFilter();
    }

    async void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionList.SelectedItem is not GameVersionInfo v) return;
        VersionList.SelectedItem = null; // 清掉選取，返回後可以再點同一項
        await OpenConfigAsync(v);
    }

    // ---------- 實例目錄 ----------
    void LoadRoots(string selectPath)
    {
        var all = InstanceRoots.All;
        RootBox.ItemsSource = all;
        RootBox.SelectedItem = all.FirstOrDefault(r => string.Equals(r.Path, selectPath, StringComparison.OrdinalIgnoreCase)) ?? all[0];
    }

    void Root_Changed(object sender, SelectionChangedEventArgs e) { if (_picked is not null) UpdateSummary(); }

    void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "選擇（或新建）一個資料夾當作實例目錄" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try { var r = InstanceRoots.Add(dlg.FolderName); LoadRoots(r.Path); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "新增實例目錄"); }
    }

    // ---------- 步驟 2：設定並安裝 ----------
    async Task OpenConfigAsync(GameVersionInfo v)
    {
        _picked = v;
        _loader = LoaderType.Vanilla;
        _nameTouched = false;
        _loaderVersions.Clear();
        VersionTitle.Text = $"Minecraft {v.Id}（{v.TypeText}）";
        LoadRoots(InstanceRoots.Active.Path);
        ResetInstallUi();
        ListStep.Visibility = Visibility.Collapsed;
        ConfigStep.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(ConfigStep, 12, 220);
        foreach (var l in Remote) { _chips[l].IsEnabled = false; _chips[l].ToolTip = null; }
        RefreshChips();
        UpdateName();
        UpdateSummary();

        // 並行查詢四種載入器在這個 Minecraft 版本有沒有可用版本；沒有的標灰
        LoaderHint.Text = "正在檢查這個版本支援哪些載入器…";
        var token = ++_token;
        var results = await Task.WhenAll(Remote.Select(async l =>
        {
            try { return (Loader: l, Versions: await LoaderVersionService.ListAsync(l, v.Id)); }
            catch { return (Loader: l, Versions: (IReadOnlyList<string>)Array.Empty<string>()); }
        }));
        if (token != _token) return; // 期間使用者換了版本
        foreach (var r in results)
        {
            _loaderVersions[r.Loader] = r.Versions;
            _chips[r.Loader].IsEnabled = r.Versions.Count > 0;
            _chips[r.Loader].ToolTip = r.Versions.Count > 0 ? null : $"{r.Loader} 沒有支援 Minecraft {v.Id} 的版本";
        }
        var any = results.Any(r => r.Versions.Count > 0);
        LoaderHint.Text = any ? "灰色的載入器＝這個 Minecraft 版本沒有可用的版本。"
            : "這個版本沒有查到任何載入器（太舊／太新，或目前沒有網路）。可以先建立原版。";
    }

    void RefreshChips()
    {
        foreach (var (l, b) in _chips)
        {
            b.Style = (Style)FindResource(l == _loader ? "ArtButton" : "GhostButton");
            b.Height = 32; b.Padding = new Thickness(16, 0, 16, 0);
        }
    }

    async Task SelectLoaderAsync(LoaderType l)
    {
        if (_busy) return;
        _loader = l;
        RefreshChips();
        if (l == LoaderType.Vanilla) LoaderVersionRow.Visibility = Visibility.Collapsed;
        else
        {
            LoaderVersionRow.Visibility = Visibility.Visible;
            LoaderVersionBox.ItemsSource = _loaderVersions.TryGetValue(l, out var list) ? list : Array.Empty<string>();
            LoaderVersionBox.SelectedIndex = LoaderVersionBox.Items.Count > 0 ? 0 : -1;
        }
        UpdateName();
        UpdateSummary();
        await Task.CompletedTask;
    }

    void LoaderVersion_Changed(object sender, SelectionChangedEventArgs e) => UpdateSummary();

    string AutoName() => _picked is null ? "" : _loader == LoaderType.Vanilla ? $"Minecraft {_picked.Id}" : $"{_loader} {_picked.Id}";

    void UpdateName()
    {
        if (_nameTouched) return;
        _settingName = true;
        try { NameBox.Text = AutoName(); } finally { _settingName = false; }
    }

    void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_settingName) _nameTouched = true; // 使用者自己改過名稱，之後換載入器不再自動覆蓋
        UpdateSummary();
    }

    void UpdateSummary()
    {
        if (_picked is null) return;
        var lv = _loader == LoaderType.Vanilla ? "原版" : $"{_loader} {LoaderVersionBox.SelectedItem as string ?? "（沒有可用版本）"}";
        SummaryText.Text = $"建立「{NameBox.Text.Trim()}」：Minecraft {_picked.Id} · {lv}\n會先下載遊戲檔案與載入器，完成後在主畫面按「啟動」即可。\n建立位置：{(RootBox.SelectedItem as InstanceRoot)?.Path ?? InstanceRoots.Active.Path}";
    }

    void ResetInstallUi()
    {
        InstallBar.Visibility = Visibility.Collapsed;
        InstallBar.Value = 0;
        StageText.Text = "";
        InstallButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        AgainButton.Visibility = Visibility.Collapsed;
        LoaderVersionRow.Visibility = Visibility.Collapsed;
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        NameBox.IsEnabled = LoaderChips.IsEnabled = LoaderVersionBox.IsEnabled = BackButton.IsEnabled = !busy;
        InstallButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        InstallBar.Visibility = Visibility.Visible;
    }

    static string Unique(string name, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(name)) return name;
        for (var i = 2; ; i++) if (!set.Contains($"{name} ({i})")) return $"{name} ({i})";
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_instances is null || _installer is null || _picked is null || _busy) return;
        var name = NameBox.Text.Trim();
        if (name.Length == 0) { StageText.Text = "請輸入實例名稱。"; return; }
        string? loaderVersion = null;
        if (_loader != LoaderType.Vanilla)
        {
            loaderVersion = LoaderVersionBox.SelectedItem as string;
            if (loaderVersion is null) { StageText.Text = "這個載入器沒有可用版本，請換一個載入器或版本。"; return; }
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        InstanceConfig? created = null;
        try
        {
            name = Unique(name, (await _instances.ListAsync()).Select(i => i.Name));
            StageText.Text = "建立實例…";
            if (RootBox.SelectedItem is InstanceRoot chosen) InstanceRoots.SetActive(chosen.Path);   // 新實例建立在選的目錄
            created = await _instances.CreateAsync(new InstanceConfig { Name = name, McVersion = _picked.Id, Loader = _loader, LoaderVersion = loaderVersion });
            InstanceCreated?.Invoke();
            var progress = new Progress<(string Stage, double Percent)>(p =>
            {
                StageText.Text = p.Percent > 0 ? $"{p.Stage}  {p.Percent:0}%" : p.Stage;
                InstallBar.Value = p.Percent;
            });
            await _installer.PrepareAsync(created.Id, progress, _cts.Token);
            InstallBar.Value = 100;
            StageText.Text = $"完成：「{name}」已建立並安裝好。回到主畫面選它，按「啟動」即可。";
            AgainButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            StageText.Text = created is null ? "已取消。"
                : $"已取消下載。實例「{created.Name}」已經建立，遊戲檔案會在第一次啟動時繼續下載。";
            AgainButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            StageText.Text = created is null ? "建立失敗：" + ex.Message
                : $"實例「{created.Name}」已建立，但預先下載失敗（第一次啟動時會再試一次）：{ex.Message}";
            AgainButton.Visibility = Visibility.Visible;
        }
        finally
        {
            SetBusy(false);
            InstallButton.Visibility = Visibility.Collapsed; // 做完（成功、失敗或取消）就只留「再建一個」，避免重複建立
            CancelButton.Visibility = Visibility.Collapsed;
            _cts?.Dispose(); _cts = null;
            if (created is null) InstallButton.Visibility = Visibility.Visible; // 連實例都沒建成才允許直接重試
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    void BackToList_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _token++;
        ConfigStep.Visibility = Visibility.Collapsed;
        ListStep.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(ListStep, -12, 200);
    }
}
