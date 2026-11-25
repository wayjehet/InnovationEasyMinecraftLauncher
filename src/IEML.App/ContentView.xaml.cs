using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// 已安裝清單的一列：檔案資訊 + 可用更新（有的話）。
public class InstalledRow : INotifyPropertyChanged
{
    public InstalledItem Item { get; set; } = null!;
    UpdateInfo? _update;
    public UpdateInfo? Update { get => _update; set { _update = value; PropertyChanged?.Invoke(this, new(nameof(UpdateText))); } }
    public string UpdateText => _update is null ? "" : $"可更新 → {_update.VersionName}";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ContentView : UserControl
{
    readonly IModrinthService _svc;
    readonly InstanceConfig? _inst;
    public ObservableCollection<ContentHit> Results { get; } = new();
    public ObservableCollection<InstalledRow> Installed { get; } = new();

    static readonly FilterOption All = new("", "全部");
    int _offset, _total;
    ModrinthQuery? _last;
    bool _loading;

    public ContentView(InstanceConfig? inst, IModrinthService svc)
    {
        _inst = inst; _svc = svc;
        InitializeComponent(); DataContext = this;
        Detail.BackRequested += CloseDetail;
        Info.Text = inst is null ? "未選擇實例（只能安裝整合包）" : $"安裝到：{inst.Name}（{inst.McVersion} / {inst.Loader}）";
        SortBox.ItemsSource = new[]
        {
            new FilterOption("relevance", "相關度"), new FilterOption("downloads", "下載數"),
            new FilterOption("follows", "追蹤數"), new FilterOption("newest", "最新發布"), new FilterOption("updated", "最近更新"),
        };
        SortBox.SelectedIndex = 0;
        EnvBox.ItemsSource = new[] { All, new FilterOption("client", "客戶端可用"), new FilterOption("server", "伺服器可用") };
        EnvBox.SelectedIndex = 0;
        if (inst is not null) McBox.Items.Add(inst.McVersion);
        _ = LoadMcVersionsAsync();
        _ = RefreshFiltersAsync();
    }

    // MC 版本下拉：載入正式版清單（失敗就用內建的幾個常用版本）；不改動目前輸入框的文字
    async Task LoadMcVersionsAsync()
    {
        try
        {
            var list = await VersionService.ListAsync(false);
            var keep = McBox.Text;
            McBox.Items.Clear();
            foreach (var v in list) McBox.Items.Add(v);
            McBox.Text = keep;
        }
        catch { }
    }

    /// 只管理已安裝的內容（實例設置用）：隱藏「搜尋與安裝」，直接顯示「已安裝管理」。下載請到主畫面的下載頁。
    public void ManageOnly()
    {
        if (Tabs.Items.Count > 0 && Tabs.Items[0] is TabItem t0) t0.Visibility = Visibility.Collapsed;
        Tabs.SelectedIndex = 1;
        ReloadInstalled();
    }

    /// 鎖定成單一類型（實例設置頁的「模組／光影／資源包」分頁用）：隱藏類型下拉，只管這一種。
    public void LockKind(ContentKind k)
    {
        foreach (var box in new[] { KindBox, InstKindBox })
        {
            foreach (ComboBoxItem it in box.Items)
                if ((string)it.Tag == k.ToString()) { box.SelectedItem = it; break; }
            box.Visibility = Visibility.Collapsed;
        }
    }

    // 與 InstanceService.GetInstanceDir 相同（會依「實例目錄」清單找實際位置）
    static string InstDir(Guid id) => InstanceRoots.DirFor(id);

    ContentKind Kind => Enum.Parse<ContentKind>((string)((ComboBoxItem)KindBox.SelectedItem).Tag);
    ContentKind InstKind => Enum.Parse<ContentKind>((string)((ComboBoxItem)InstKindBox.SelectedItem).Tag);

    // ---------- 篩選 ----------
    async void KindBox_Changed(object s, SelectionChangedEventArgs e)
    {
        if (CategoryBox is null || McBox is null || LoaderBox is null) return; // 還在 InitializeComponent
        await RefreshFiltersAsync();
    }

    async Task RefreshFiltersAsync()
    {
        var kind = Kind;
        // 光影／資源包／模組預設鎖定實例版本；整合包預設全部（它自己會建立新實例）
        McBox.Text = kind == ContentKind.Modpack || _inst is null ? "" : _inst.McVersion;
        EnvPanel.Visibility = kind is ContentKind.Mod or ContentKind.Modpack ? Visibility.Visible : Visibility.Collapsed;
        LoaderLabel.Text = kind == ContentKind.Shader ? "光影載入器" : "載入器";

        var cats = await _svc.GetCategoriesAsync(kind);
        CategoryBox.ItemsSource = new[] { All }.Concat(cats).ToList(); CategoryBox.SelectedIndex = 0;

        var loaders = await _svc.GetLoadersAsync(kind);
        LoaderPanel.Visibility = loaders.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LoaderBox.ItemsSource = new[] { All }.Concat(loaders).ToList();
        // 模組預設用實例的載入器；整合包／光影預設全部
        var def = kind == ContentKind.Mod && _inst is not null && _inst.Loader != LoaderType.Vanilla ? _inst.Loader.ToString().ToLowerInvariant() : "";
        LoaderBox.SelectedValue = def; if (LoaderBox.SelectedIndex < 0) LoaderBox.SelectedIndex = 0;
    }

    void ResetFilters_Click(object s, RoutedEventArgs e) => _ = RefreshFiltersAsync();

    ModrinthQuery BuildQuery(int offset) => new(QueryBox.Text, Kind, McBox.Text,
        (string?)LoaderBox.SelectedValue is { Length: > 0 } l ? l : null,
        (string?)CategoryBox.SelectedValue is { Length: > 0 } c ? c : null,
        EnvPanel.Visibility == Visibility.Visible && (string?)EnvBox.SelectedValue is { Length: > 0 } env ? env : null,
        (string?)SortBox.SelectedValue ?? "relevance", offset);

    // ---------- 搜尋 ----------
    async void Search_Click(object s, RoutedEventArgs e)
    {
        if (_inst is null && Kind != ContentKind.Modpack) { StatusText.Text = "請先在主畫面選一個實例"; return; }
        Results.Clear(); _offset = 0;
        await LoadPageAsync();
    }

    async void More_Click(object s, RoutedEventArgs e) => await LoadPageAsync();

    async Task LoadPageAsync()
    {
        if (_loading) return;
        _loading = true; StatusText.Text = "搜尋中…";
        try
        {
            var q = BuildQuery(_offset); _last = q;
            var page = await _svc.SearchAsync(q);
            foreach (var h in page.Hits) Results.Add(h);
            _offset += page.Hits.Count; _total = page.Total;
            StatusText.Text = $"共 {_total} 個結果，已顯示 {Results.Count} 個";
            MoreBtn.Visibility = Results.Count < _total ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { StatusText.Text = "搜尋失敗：" + ex.Message; }
        finally { _loading = false; }
    }

    // ---------- 項目詳情（點項目 → 所有版本）----------
    async void Results_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ContentHit hit) return;
        ResultsList.SelectedItem = null; // 清掉選取，返回後可再點同一項
        if (_svc is not IVersionSource src) { StatusText.Text = "目前的來源不支援選版本"; return; }
        MainDock.Visibility = Visibility.Collapsed;
        Detail.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(Detail, 10, 200);
        await Detail.ShowAsync(src, hit, _inst);
    }

    void CloseDetail()
    {
        Detail.Visibility = Visibility.Collapsed;
        MainDock.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(MainDock, -10, 180);
    }

    async void Install_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not ContentHit hit) return;
        StatusText.Text = $"安裝中：{hit.Title}…";
        try
        {
            await _svc.InstallAsync(_inst?.Id ?? Guid.Empty, hit.ProjectId, hit.Kind);
            StatusText.Text = hit.Kind == ContentKind.Modpack ? $"已建立新實例：{hit.Title}" : $"已安裝：{hit.Title}";
        }
        catch (Exception ex) { StatusText.Text = "安裝失敗：" + ex.Message; }
    }

    // ---------- 拖放匯入（資源包 .zip／光影 .zip／模組 .jar）----------
    void Window_PreviewDragOver(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy; e.Handled = true;
    }

    void Window_PreviewDrop(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (_inst is null) { StatusText.Text = "請先在主畫面選一個實例，再把檔案拖進來"; return; }
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var dir = InstDir(_inst.Id);
        int ok = 0; var errors = new List<string>(); ContentKind? last = null;
        foreach (var f in files)
        {
            // .jar → 模組；.zip → 目前選的是光影就當光影，否則當資源包
            var cur = Tabs.SelectedIndex == 1 ? InstKind : Kind;
            var kind = System.IO.Path.GetExtension(f).Equals(".jar", StringComparison.OrdinalIgnoreCase) ? ContentKind.Mod
                     : cur == ContentKind.Shader ? ContentKind.Shader : ContentKind.ResourcePack;
            var r = InstalledContentService.Import(dir, kind, f);
            if (r.Ok) { ok++; last = kind; } else errors.Add(r.Message);
        }
        foreach (var m in errors) GameLog.Append("匯入失敗：" + m);
        var msg = (ok > 0 ? $"已加入 {ok} 個檔案。" : "") + (errors.Count > 0 ? string.Join("\n", errors) : "");
        if (last is { } k)
        {
            foreach (ComboBoxItem it in InstKindBox.Items) if ((string)it.Tag == k.ToString()) InstKindBox.SelectedItem = it;
            Tabs.SelectedIndex = 1;
            ReloadInstalled();      // 會改寫狀態列，所以訊息要在這之後再設
        }
        StatusText.Text = msg;
    }

    // ---------- 已安裝管理 ----------
    void Tabs_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs) return;       // 避免內部 ComboBox 的事件冒泡進來
        if (Tabs.SelectedIndex == 1) ReloadInstalled();
    }

    void InstKind_Changed(object s, SelectionChangedEventArgs e) { if (IsLoaded) ReloadInstalled(); }

    void ReloadInstalled()
    {
        Installed.Clear(); UpdateAllBtn.IsEnabled = false;
        if (_inst is null) { StatusText.Text = "未選擇實例"; return; }
        var items = InstalledContentService.List(InstDir(_inst.Id), InstKind);
        foreach (var it in items) Installed.Add(new InstalledRow { Item = it });
        StatusText.Text = $"共 {items.Count} 個檔案（取消勾選＝停用，不會刪除）";
    }

    void ToggleInstalled_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not InstalledRow row) return;
        try { InstalledContentService.SetEnabled(row.Item, ((CheckBox)s).IsChecked == true); }
        catch (Exception ex) { StatusText.Text = "切換失敗：" + ex.Message; }
        ReloadInstalled();
    }

    async void DeleteInstalled_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not InstalledRow row) return;
        if (!await Dialogs.ConfirmAsync("刪除", $"確定刪除「{row.Item.DisplayName}」？", "刪除")) return;
        try { InstalledContentService.Delete(row.Item); } catch (Exception ex) { StatusText.Text = "刪除失敗：" + ex.Message; }
        ReloadInstalled();
    }

    void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        if (_inst is null) return;
        var dir = System.IO.Path.Combine(InstDir(_inst.Id), InstalledContentService.FolderFor(InstKind));
        System.IO.Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    async void CheckUpdates_Click(object s, RoutedEventArgs e)
    {
        if (_inst is null) return;
        StatusText.Text = "檢查更新中…（只比對正式版）";
        try
        {
            var ups = await _svc.CheckUpdatesAsync(_inst.Id, InstKind);
            var map = ups.ToDictionary(u => u.OldPath, StringComparer.OrdinalIgnoreCase);
            foreach (var r in Installed) r.Update = map.TryGetValue(r.Item.Path, out var u) ? u : null;
            UpdateAllBtn.IsEnabled = ups.Count > 0;
            StatusText.Text = ups.Count == 0 ? "全部都是最新版" : $"有 {ups.Count} 個可更新";
        }
        catch (Exception ex) { StatusText.Text = "檢查失敗：" + ex.Message; }
    }

    async void UpdateAll_Click(object s, RoutedEventArgs e)
    {
        var todo = Installed.Where(r => r.Update != null).Select(r => r.Update!).ToList();
        int ok = 0; var fail = new List<string>();
        foreach (var u in todo)
        {
            StatusText.Text = $"更新中 {ok + fail.Count + 1}/{todo.Count}：{u.NewFileName}";
            try { await _svc.ApplyUpdateAsync(u); ok++; }
            catch (Exception ex) { fail.Add($"{u.NewFileName}（{ex.Message}）"); }
        }
        ReloadInstalled();
        StatusText.Text = fail.Count == 0 ? $"已更新 {ok} 個" : $"已更新 {ok} 個，失敗 {fail.Count}：{string.Join("；", fail)}";
    }
}
