using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// 項目詳情頁：列出一個 Modrinth 專案的所有版本，選一個安裝（含前置模組）。
/// 下載頁與實例設置頁的內容分頁共用；覆蓋在搜尋結果上，「← 返回結果」觸發 BackRequested，由宿主負責切換顯示。
public partial class ProjectDetailView : UserControl
{
    sealed class OptionalDep
    {
        public string ProjectId { get; init; } = "";
        public string Label { get; init; } = "";
        public bool Selected { get; set; }
    }

    IVersionSource? _src;
    ContentHit? _hit;
    InstanceConfig? _target;
    int _load;
    readonly Dictionary<string, string> _depTitles = new();

    public event Action? BackRequested;
    /// 成功安裝（或建立整合包實例）後觸發。
    public event Action? Installed;

    public ProjectDetailView() => InitializeComponent();

    /// src＝版本來源（Modrinth 或 CurseForge）；target＝要安裝到的實例，整合包不需要（會建立新實例），可傳 null。
    public async Task ShowAsync(IVersionSource src, ContentHit hit, InstanceConfig? target)
    {
        _src = src; _hit = hit; _target = target;
        var isPack = hit.Kind == ContentKind.Modpack;
        TitleText.Text = hit.Title;
        DescText.Text = hit.Description;
        MatchOnly.Visibility = AutoDeps.Visibility = isPack ? Visibility.Collapsed : Visibility.Visible;
        TargetText.Text = isPack ? "安裝後會建立一個新的實例"
            : target is null ? "尚未選擇實例，請回上一頁選一個再安裝"
            : $"安裝到：{target.Name}（{target.McVersion} · {target.Loader}）";
        VersionList.ItemsSource = null;
        ClearDeps();
        InstallButton.IsEnabled = false;
        StatusText.Text = "載入版本中…";
        await LoadVersionsAsync();
    }

    async Task LoadVersionsAsync()
    {
        if (_hit is not { } hit || _src is null) return;
        var token = ++_load;
        try
        {
            var filtering = MatchOnly.IsChecked == true && hit.Kind != ContentKind.Modpack && _target is not null;
            var list = (await _src.ListVersionsAsync(hit, _target, filtering)).ToList();
            if (token != _load) return; // 期間又換了項目或篩選
            VersionList.ItemsSource = list;
            StatusText.Text = list.Count == 0
                ? (filtering ? "沒有符合目前實例的版本，可取消上方勾選看全部版本。" : "這個專案沒有可下載的版本。")
                : $"共 {list.Count} 個版本（新→舊），選一個再按安裝。";
            if (list.Count > 0) VersionList.SelectedIndex = 0;
        }
        catch (Exception ex) { if (token == _load) StatusText.Text = "載入版本失敗：" + ex.Message; }
    }

    async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _hit is null) return;
        await LoadVersionsAsync();
    }

    void ClearDeps()
    {
        RequiredText.Text = "";
        ChangelogText.Text = "";
        OptionalList.ItemsSource = null;
    }

    string DepTitle(string id) => _depTitles.TryGetValue(id, out var t) && t != "" ? t : id;

    async void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClearDeps();
        if (VersionList.SelectedItem is not ContentVersion v || _src is null) { InstallButton.IsEnabled = false; return; }
        InstallButton.IsEnabled = true;
        var log = (v.Changelog ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        ChangelogText.Text = log.Length == 0 ? "" : "更新內容：" + (log.Length > 160 ? log[..160] + "…" : log);

        var req = v.Deps.Where(d => d.Kind == "required" && d.ProjectId != null).Select(d => d.ProjectId!).Distinct().ToList();
        var opt = v.Deps.Where(d => d.Kind == "optional" && d.ProjectId != null).Select(d => d.ProjectId!).Distinct().ToList();
        var need = req.Concat(opt).Where(id => !_depTitles.ContainsKey(id)).ToList();
        if (need.Count > 0)
        {
            try { foreach (var kv in await _src.GetProjectTitlesAsync(need)) _depTitles[kv.Key] = kv.Value; }
            catch { /* 查不到名稱就顯示 id，不影響安裝 */ }
        }
        if (VersionList.SelectedItem != v) return; // 等待期間使用者又選了別的版本
        RequiredText.Text = req.Count == 0 ? "必要前置：無" : "必要前置：" + string.Join("、", req.Select(DepTitle));
        OptionalList.ItemsSource = opt.Select(id => new OptionalDep { ProjectId = id, Label = "可選前置：" + DepTitle(id) }).ToList();
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_hit is not { } hit || VersionList.SelectedItem is not ContentVersion v || _src is null) return;
        if (hit.Kind != ContentKind.Modpack && _target is null) { StatusText.Text = "請先回上一頁選一個實例。"; return; }
        InstallButton.IsEnabled = false;
        StatusText.Text = $"安裝中：{hit.Title} {v.Number}…";
        try
        {
            var optional = (OptionalList.ItemsSource as IEnumerable<OptionalDep>)?.Where(o => o.Selected).Select(o => o.ProjectId).ToList()
                           ?? new List<string>();
            var warnings = await _src.InstallVersionAsync(_target, hit, v, AutoDeps.IsChecked == true, optional);
            StatusText.Text = (hit.Kind == ContentKind.Modpack ? $"已建立整合包實例：{hit.Title}" : $"已安裝：{hit.Title} {v.Number}")
                              + (warnings.Count > 0 ? "\n" + string.Join("\n", warnings) : "");
            Installed?.Invoke();
        }
        catch (Exception ex) { StatusText.Text = "安裝失敗：" + ex.Message; }
        finally { InstallButton.IsEnabled = true; }
    }

    void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
}
