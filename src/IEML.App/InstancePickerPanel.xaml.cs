using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// 下拉清單的一列：「全部目錄」、每個實例目錄、最後一列「＋ 新增目錄…」。
public sealed class RootFilterItem
{
    public string Label { get; init; } = "";
    public string FullPath { get; init; } = "";      // 灰字完整路徑（全部／新增 為空字串，樣式會自動隱藏）
    public InstanceRoot? Root { get; init; }
    public bool IsAdd { get; init; }
    public bool HasMenu => Root is not null;         // 只有真正的目錄才有「⋯」
}

/// 清單裡的一個實例（多帶一行「版本 · 載入器 · 所屬目錄」）。
public sealed class PickerItem
{
    public PickerItem(InstanceConfig inst, string detail) { Inst = inst; Detail = detail; }
    public InstanceConfig Inst { get; }
    public string Name => Inst.Name;
    public string Detail { get; }
}

/// 實例選擇面板：DataContext 直接用 MainViewModel（刪除實例等命令綁在上面）。
/// 上方的「目錄」下拉可以篩選實例，也是實例目錄的管理入口（新增／設為使用中／開啟／移除）。
public partial class InstancePickerPanel : UserControl
{
    readonly MainViewModel _vm;
    readonly Action<bool> _close;

    readonly RootFilterItem _all = new() { Label = "全部目錄" };
    readonly RootFilterItem _addItem = new() { Label = "＋ 新增目錄…", IsAdd = true };
    RootFilterItem _current;                  // 目前的篩選
    InstanceRoot? _sheetRoot;                 // 管理面板正在處理哪個目錄
    bool _syncing, _rebuildPending, _hooked;
    readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public InstancePickerPanel(MainViewModel vm, Action<bool> close)
    {
        InitializeComponent();
        DataContext = vm; _vm = vm; _close = close; _current = _all;
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); Notice.Visibility = Visibility.Collapsed; };
        Loaded += (_, _) => { Hook(); LoadRoots(); Rebuild(); };
        Unloaded += (_, _) => Unhook();
    }

    // ---------- 事件掛接（面板關閉時一定要拆掉，否則 MainViewModel 會一直抓著它） ----------
    void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _vm.Instances.CollectionChanged += OnInstancesChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
        InstanceRoots.Changed += OnRootsChanged;
    }

    void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        _vm.Instances.CollectionChanged -= OnInstancesChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        InstanceRoots.Changed -= OnRootsChanged;
        _noticeTimer.Stop();
    }

    void OnInstancesChanged(object? s, NotifyCollectionChangedEventArgs e) => ScheduleRebuild();

    void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedInstance)) SyncSelection();
    }

    void OnRootsChanged() => Dispatcher.BeginInvoke(new Action(() => { LoadRoots(); ScheduleRebuild(); }));

    // 重新整理時 Instances 會先清空再一筆筆加回，合併成一次重建
    void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _rebuildPending = false; Rebuild(); }));
    }

    // ---------- 目錄下拉 ----------
    void LoadRoots()
    {
        var active = InstanceRoots.Active;
        var items = new List<RootFilterItem> { _all };
        foreach (var r in InstanceRoots.All)
        {
            var isActive = SamePath(r.Path, active.Path);
            items.Add(new RootFilterItem { Label = r.Name + (isActive ? "　● 使用中" : ""), FullPath = r.Path, Root = r });
        }
        items.Add(_addItem);

        // 篩選的那個目錄如果還在就維持，不在了（被移除）就回到「全部」
        _current = _current.Root is null ? _all
            : items.FirstOrDefault(i => i.Root is not null && SamePath(i.Root.Path, _current.Root!.Path)) ?? _all;

        _syncing = true;
        RootBox.ItemsSource = items;
        RootBox.SelectedItem = _current;
        _syncing = false;
    }

    void RootBox_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (_syncing || RootBox.SelectedItem is not RootFilterItem it) return;
        if (it.IsAdd)
        {
            // 「＋ 新增目錄…」不是篩選條件：先把選取還原，再開資料夾選擇視窗
            _syncing = true; RootBox.SelectedItem = _current; _syncing = false;
            Dispatcher.BeginInvoke(new Action(AddRoot));
            return;
        }
        _current = it;
        Notice.Visibility = Visibility.Collapsed;
        Rebuild();
    }

    async void AddRoot()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "選擇（或新建）一個資料夾當作實例目錄" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var r = InstanceRoots.Add(dlg.FolderName);
            LoadRoots();
            await _vm.RefreshInstancesPublicAsync();
            SelectRoot(r.Path);
            ShowNotice($"已新增目錄「{r.Name}」。");
        }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }

    void SelectRoot(string path)
    {
        if (RootBox.ItemsSource is not List<RootFilterItem> items) return;
        var hit = items.FirstOrDefault(i => i.Root is not null && SamePath(i.Root.Path, path));
        if (hit is null) return;
        _current = hit;
        _syncing = true; RootBox.SelectedItem = hit; _syncing = false;
        Rebuild();
    }

    // ---------- 實例清單 ----------
    void Rebuild()
    {
        var roots = InstanceRoots.All;
        var multi = roots.Count > 1;
        var items = new List<PickerItem>();
        foreach (var i in _vm.Instances)
        {
            var rootPath = Path.GetDirectoryName(InstanceRoots.DirFor(i.Id)) ?? "";
            if (_current.Root is not null && !SamePath(_current.Root.Path, rootPath)) continue;
            var detail = $"{i.McVersion}  ·  {i.Loader}";
            if (multi && roots.FirstOrDefault(r => SamePath(r.Path, rootPath)) is { } rt) detail += $"  ·  {rt.Name}";   // 只有多個目錄時才標出所屬目錄
            items.Add(new PickerItem(i, detail));
        }

        _syncing = true;
        InstList.ItemsSource = items;
        InstList.SelectedItem = items.FirstOrDefault(x => x.Inst.Id == _vm.SelectedInstance?.Id);
        _syncing = false;

        EmptyText.Text = _current.Root is null ? "還沒有實例，按「新增實例」建立。" : "這個目錄裡沒有實例。";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 選取狀態以 MainViewModel.SelectedInstance 為準；這裡不直接雙向綁定，
    // 否則切換目錄篩選時，清單把選取清掉會連帶把主畫面選的實例也清掉。
    void InstList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (InstList.SelectedItem is PickerItem p) _vm.SelectedInstance = p.Inst;
    }

    void SyncSelection()
    {
        if (InstList.ItemsSource is not List<PickerItem> items) return;
        _syncing = true;
        InstList.SelectedItem = items.FirstOrDefault(x => x.Inst.Id == _vm.SelectedInstance?.Id);
        _syncing = false;
    }

    void Ok_Click(object s, RoutedEventArgs e) => _close(true);
    void Add_Click(object s, RoutedEventArgs e) { _close(false); _vm.AddInstanceCommand.Execute(null); }   // 先關掉選擇面板，再開新增面板
    void List_DoubleClick(object s, MouseButtonEventArgs e) { if (InstList.SelectedItem is PickerItem) _close(true); }

    // ---------- 目錄管理面板（點下拉清單某列的「⋯」） ----------
    void RootMenu_Click(object s, RoutedEventArgs e)
    {
        e.Handled = true;   // 不要讓這一下被當成「選了這一列」
        if (((FrameworkElement)s).DataContext is not RootFilterItem { Root: { } root }) return;
        RootBox.IsDropDownOpen = false;
        _sheetRoot = root;
        SheetTitle.Text = root.Name;
        SheetPath.Text = root.Path;
        SheetUse.Visibility = SamePath(root.Path, InstanceRoots.Active.Path) ? Visibility.Collapsed : Visibility.Visible;
        SheetRemove.Visibility = InstanceRoots.IsDefault(root) ? Visibility.Collapsed : Visibility.Visible;   // 預設目錄不能移除
        SheetConfirmText.Visibility = Visibility.Collapsed;
        SheetConfirm.Visibility = Visibility.Collapsed;
        SheetMain.Visibility = Visibility.Visible;
        Sheet.Visibility = Visibility.Visible;
    }

    void SheetClose_Click(object s, RoutedEventArgs e) => CloseSheet();

    void CloseSheet()
    {
        Sheet.Visibility = Visibility.Collapsed;
        _sheetRoot = null;
    }

    void SheetUse_Click(object s, RoutedEventArgs e)
    {
        if (_sheetRoot is not { } r) return;
        try { InstanceRoots.SetActive(r.Path); ShowNotice($"新實例會建立在「{r.Name}」。"); }   // Changed 事件會自動刷新下拉的「● 使用中」
        catch (Exception ex) { ShowNotice(ex.Message); }
        CloseSheet();
    }

    void SheetOpen_Click(object s, RoutedEventArgs e)
    {
        if (_sheetRoot is not { } r) return;
        try { Directory.CreateDirectory(r.Path); Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true }); }
        catch (Exception ex) { ShowNotice("無法開啟資料夾：" + ex.Message); }
        CloseSheet();
    }

    void SheetRemove_Click(object s, RoutedEventArgs e)
    {
        if (_sheetRoot is not { } r) return;
        SheetConfirmText.Text = $"要從清單移除「{r.Name}」嗎？\n這個目錄裡的實例會從清單消失，但檔案不會被刪除；之後可以再把同一個資料夾添加回來。";
        SheetConfirmText.Visibility = Visibility.Visible;
        SheetMain.Visibility = Visibility.Collapsed;
        SheetConfirm.Visibility = Visibility.Visible;
    }

    async void SheetConfirmRemove_Click(object s, RoutedEventArgs e)
    {
        if (_sheetRoot is not { } r) return;
        CloseSheet();
        try
        {
            InstanceRoots.Remove(r.Path);              // 篩選的就是這個目錄時，LoadRoots 會自動回到「全部目錄」
            LoadRoots();
            await _vm.RefreshInstancesPublicAsync();
            ShowNotice($"已從清單移除「{r.Name}」（檔案沒有被刪除）。");
        }
        catch (Exception ex) { ShowNotice("無法移除：" + ex.Message); }
    }

    // ---------- 小工具 ----------
    void ShowNotice(string text)
    {
        Notice.Text = text;
        Notice.Visibility = Visibility.Visible;
        _noticeTimer.Stop(); _noticeTimer.Start();
    }

    static string Norm(string p)
    {
        try { return Path.GetFullPath(p).TrimEnd('\\', '/'); } catch { return p.Trim(); }
    }

    static bool SamePath(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
}
