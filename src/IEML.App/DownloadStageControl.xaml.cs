using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

public partial class DownloadStageControl : UserControl
{
    readonly IInstanceService _instances;
    readonly IModrinthService _modrinth;
    readonly CurseForgeService _curseForge;
    readonly ObservableCollection<ContentHit> _results = new();

    public DownloadStageControl(IInstanceService instances)
    {
        InitializeComponent();
        _instances = instances;
        _modrinth = new ModrinthService(instances);
        _curseForge = new CurseForgeService(instances);
        ResultsList.ItemsSource = _results;
        LoaderBox.ItemsSource = Enum.GetValues<LoaderType>();
        LoaderBox.SelectedItem = LoaderType.Vanilla;
        Loaded += async (_, _) => { await LoadVersionsAsync(); await LoadInstancesAsync(); };
        LoaderBox.SelectionChanged += (_, _) => { UpdateSummary(); UpdateLoaderVersionHint(); };
        VersionBox.SelectionChanged += (_, _) => { UpdateSummary(); UpdateLoaderVersionHint(); };
    }

    async Task LoadVersionsAsync()
    {
        try
        {
            var include = IncludeSnapshots.IsChecked == true;
            var versions = await VersionService.ListAsync(include);
            VersionBox.ItemsSource = versions;
            if (versions.Count > 0) VersionBox.SelectedIndex = 0;
            UpdateSummary();
        }
        catch (Exception ex) { GameStatus.Text = ex.Message; }
    }

    async Task LoadInstancesAsync()
    {
        var instances = await _instances.ListAsync();
        InstanceBox.ItemsSource = instances;
        InstanceBox.SelectedIndex = 0;
        UpdateSummary();
    }

    async void VersionsChanged(object sender, RoutedEventArgs e) => await LoadVersionsAsync();

    void UpdateSummary()
    {
        var mc = VersionBox.SelectedItem as string ?? VersionBox.Text;
        var loader = LoaderBox.SelectedItem is LoaderType l ? l : LoaderType.Vanilla;
        var loaderText = LoaderVersionBox.Text.Trim();
        NameBox.Text = string.IsNullOrWhiteSpace(NameBox.Text) && !string.IsNullOrWhiteSpace(mc)
            ? $"{loader} {mc}" : NameBox.Text;
        SummaryText.Text = loader == LoaderType.Vanilla
            ? $"建立「{NameBox.Text}」· Minecraft {mc} · 原版"
            : $"建立「{NameBox.Text}」· Minecraft {mc} · {loader}{(string.IsNullOrEmpty(loaderText) ? "" : " " + loaderText)}";
    }

    async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var mc = VersionBox.SelectedItem as string ?? VersionBox.Text.Trim();
        var loader = LoaderBox.SelectedItem is LoaderType l ? l : LoaderType.Vanilla;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mc))
        {
            GameStatus.Text = "請輸入實例名稱與 Minecraft 版本";
            return;
        }
        CreateButton.IsEnabled = false;
        InstallProgress.Value = 15;
        try
        {
            await _instances.CreateAsync(new InstanceConfig
            {
                Name = name,
                McVersion = mc,
                Loader = loader,
                LoaderVersion = string.IsNullOrWhiteSpace(LoaderVersionBox.Text) ? null : LoaderVersionBox.Text.Trim()
            });
            InstallProgress.Value = 100;
            GameStatus.Text = "實例建立完成";
        }
        catch (Exception ex) { GameStatus.Text = "建立失敗：" + ex.Message; }
        finally { CreateButton.IsEnabled = true; }
    }

    /// 地圖：開啟地圖視窗（匯入 zip／資料夾／網址、CurseForge 地圖、備份、刪除），放進「安裝到實例」選的實例。
    void Maps_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceBox.SelectedItem is not InstanceConfig inst)
        {
            MessageBox.Show(Window.GetWindow(this), "請先建立或選擇一個實例（地圖會放進該實例的 saves 資料夾）。", "地圖");
            return;
        }
        new MapsWindow(_instances, inst) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    void Tab_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        var tab = tag == "installed" ? "installed" : tag;
        GamePanel.Visibility = tab == "game" ? Visibility.Visible : Visibility.Collapsed;
        ContentPanel.Visibility = tab == "content" ? Visibility.Visible : Visibility.Collapsed;
        InstalledPanel.Visibility = tab == "installed" ? Visibility.Visible : Visibility.Collapsed;
        GameTab.Opacity = tab == "game" ? 1 : .55;
        ContentTab.Opacity = tab == "content" ? 1 : .55;
        if (InstalledTab != null) InstalledTab.Opacity = tab == "installed" ? 1 : .55;
    }

    async void Search_Click(object sender, RoutedEventArgs e)
    {
        _results.Clear();
        ContentStatus.Text = "搜尋中…";
        try
        {
            var kind = GetKind();
            var sort = (SortBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "relevance";
            var category = (CategoryBox.SelectedItem as FilterOption)?.Id;
            var loader = (LoaderFilterBox.SelectedItem as FilterOption)?.Id;
            var env = (EnvBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var mc = string.IsNullOrWhiteSpace(FilterMcBox.Text) ? null : FilterMcBox.Text.Trim();
            var source = (SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Modrinth";
            if (source == "CurseForge")
            {
                var inst = InstanceBox.SelectedItem as InstanceConfig;
                var cfLoader = inst?.Loader;
                var hits = await _curseForge.SearchAsync(QueryBox.Text.Trim(), kind, mc ?? "", cfLoader);
                foreach (var hit in hits) _results.Add(hit);
                ContentStatus.Text = $"CurseForge：已顯示 {_results.Count} 個結果";
                return;
            }

            var result = await _modrinth.SearchAsync(new ModrinthQuery(QueryBox.Text.Trim(), kind, mc, loader, category, env, sort, 0, 20));
            foreach (var hit in result.Hits) _results.Add(hit);
            ContentStatus.Text = $"共 {result.Total} 個結果，已顯示 {_results.Count} 個";
        }
        catch (Exception ex) { ContentStatus.Text = "搜尋失敗：" + ex.Message; }
    }

    async void InstallHit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ContentHit hit) return;
        ContentStatus.Text = $"安裝中：{hit.Title}";
        try
        {
            var source = (SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Modrinth";
            if (source == "CurseForge")
            {
                var inst = InstanceBox.SelectedItem as InstanceConfig;
                if (hit.Kind == ContentKind.Modpack)
                    await _curseForge.InstallModpackAsync(hit.ProjectId);
                else
                {
                    if (inst is null) throw new InvalidOperationException("請先建立或選擇安裝目標實例");
                    await _curseForge.InstallFileAsync(inst.Id, hit.ProjectId, hit.Kind);
                }
            }
            else if (hit.Kind == ContentKind.Modpack)
            {
                await _modrinth.InstallModpackAsync(hit.ProjectId);
            }
            else
            {
                var inst = InstanceBox.SelectedItem as InstanceConfig;
                if (inst is null) throw new InvalidOperationException("請先建立或選擇安裝目標實例");
                await _modrinth.InstallAsync(inst.Id, hit.ProjectId, hit.Kind);
            }
            ContentStatus.Text = hit.Kind == ContentKind.Modpack ? $"已建立整合包實例：{hit.Title}" : $"已安裝：{hit.Title}";
        }
        catch (Exception ex) { ContentStatus.Text = "安裝失敗：" + ex.Message; }
    }

    async void KindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await RefreshFiltersAsync();
    }

    void FiltersChanged(object sender, SelectionChangedEventArgs e) { }

    async Task RefreshFiltersAsync()
    {
        try
        {
            var kind = GetKind();
            var categories = await _modrinth.GetCategoriesAsync(kind);
            CategoryBox.ItemsSource = new[] { new FilterOption("", "全部分類") }.Concat(categories).ToList();
            CategoryBox.DisplayMemberPath = "Name";
            CategoryBox.SelectedValuePath = "Id";
            CategoryBox.SelectedIndex = 0;

            var loaders = await _modrinth.GetLoadersAsync(kind);
            LoaderFilterBox.ItemsSource = new[] { new FilterOption("", "全部載入器") }.Concat(loaders).ToList();
            LoaderFilterBox.DisplayMemberPath = "Name";
            LoaderFilterBox.SelectedValuePath = "Id";
            LoaderFilterBox.SelectedIndex = 0;

            FilterMcBox.ItemsSource = new[] { "", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.18.2", "1.16.5", "1.12.2", "1.8.9" };
            if (InstanceBox.SelectedItem is InstanceConfig inst) FilterMcBox.Text = inst.McVersion;
        }
        catch (Exception ex) { ContentStatus.Text = "載入篩選失敗：" + ex.Message; }
    }

    void UpdateLoaderVersionHint()
    {
        var loader = LoaderBox.SelectedItem is LoaderType l ? l : LoaderType.Vanilla;
        LoaderVersionBox.IsEnabled = loader != LoaderType.Vanilla;
        if (loader == LoaderType.Vanilla)
        {
            LoaderVersionBox.Text = "";
            LoaderVersionBox.ItemsSource = null;
        }
        else
        {
            var mc = VersionBox.SelectedItem as string ?? VersionBox.Text;
            LoaderVersionBox.ItemsSource = new[] { "自動使用最新穩定版" };
            LoaderVersionBox.Text = "自動使用最新穩定版";
        }
    }

    ContentKind GetKind() => ((KindBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
    {
        "Shader" => ContentKind.Shader,
        "ResourcePack" => ContentKind.ResourcePack,
        "Modpack" => ContentKind.Modpack,
        _ => ContentKind.Mod
    };

    async void RefreshInstalled_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var inst = InstanceBox.SelectedItem as InstanceConfig;
            if (inst is null) { InstalledStatus.Text = "請先選擇實例"; return; }
            var kind = ((InstalledKindBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Shader" => ContentKind.Shader,
                "ResourcePack" => ContentKind.ResourcePack,
                _ => ContentKind.Mod
            };
            var dir = System.IO.Path.Combine(_instances.GetInstanceDir(inst.Id), InstalledContentService.FolderFor(kind));
            var items = InstalledContentService.List(dir, kind);
            InstalledStatus.Text = $"共 {items.Count} 個已安裝項目（取消勾選＝停用，不會刪除）";
        }
        catch (Exception ex) { InstalledStatus.Text = "讀取失敗：" + ex.Message; }
    }

    async void ImportLocal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇本機整合包",
            Filter = "整合包 (*.zip;*.mrpack)|*.zip;*.mrpack|所有檔案 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        InstalledStatus.Text = "匯入中…";
        try
        {
            var cf = new CurseForgeService(_instances);
            var modrinth = new ModrinthService(_instances);
            if (dlg.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                await modrinth.ImportMrpackAsync(dlg.FileName, System.IO.Path.GetFileNameWithoutExtension(dlg.FileName), default);
            }
            else
            {
                await cf.ImportLocalAsync(dlg.FileName);
            }
            InstalledStatus.Text = "本機整合包匯入完成";
            await LoadInstancesAsync();
        }
        catch (Exception ex) { InstalledStatus.Text = "匯入失敗：" + ex.Message; }
    }
}
