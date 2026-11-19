using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

public partial class MapsWindow : Window
{
    readonly MapService _maps;
    readonly CurseForgeService _cf;
    readonly InstanceConfig _inst;
    bool _busy;
    int _offset;
    public ObservableCollection<WorldInfo> Worlds { get; } = new();
    public ObservableCollection<MapHit> Hits { get; } = new();

    public MapsWindow(IInstanceService instances, InstanceConfig inst)
    {
        _maps = new MapService(instances);
        _cf = new CurseForgeService(instances);
        _inst = inst;
        InitializeComponent();
        DataContext = this;
        Info.Text = $"地圖會放進實例「{inst.Name}」（{inst.McVersion}）的 saves 資料夾，進遊戲 → 單人遊戲就能看到。建議先備份再玩。";
        McBox.Text = inst.McVersion;
        CfNote.Text = CurseForgeSettings.HasKey
            ? "搜尋 CurseForge 的地圖分類；部分作者不開放第三方啟動器下載，遇到時請到網站手動下載再匯入 zip。"
            : "需要 CurseForge API 金鑰：到主畫面「下載 → CurseForge」填入後才能搜尋。也可以先用「我的地圖」分頁匯入 zip 或貼網址。";
        Reload();
    }

    void Reload()
    {
        Worlds.Clear();
        foreach (var w in _maps.List(_inst.Id)) Worlds.Add(w);
        StatusText.Text = Worlds.Count == 0 ? "這個實例還沒有地圖" : $"共 {Worlds.Count} 張地圖";
    }

    async Task RunAsync(string title, Func<IProgress<(string Stage, double Percent)>, Task<string>> work)
    {
        if (_busy) { StatusText.Text = "已有項目正在處理中"; return; }
        _busy = true;
        Bar.Value = 0;
        Bar.Visibility = Visibility.Visible;
        try
        {
            var prog = new Progress<(string Stage, double Percent)>(p =>
            {
                StatusText.Text = $"{title}：{p.Stage}";
                Bar.Value = Math.Clamp(p.Percent, 0, 100);
            });
            var msg = await work(prog);
            Reload();
            StatusText.Text = msg;
        }
        catch (Exception ex) { StatusText.Text = $"{title}失敗：{ex.Message}"; }
        finally { _busy = false; Bar.Visibility = Visibility.Collapsed; }
    }

    async void ImportZip_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "選擇地圖壓縮檔", Filter = "地圖 (*.zip)|*.zip|所有檔案 (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        await RunAsync("匯入", async p => $"已匯入地圖：{await _maps.ImportZipAsync(_inst.Id, dlg.FileName, null, p)}");
    }

    async void ImportFolder_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "選擇地圖資料夾（裡面要有 level.dat）" };
        if (dlg.ShowDialog(this) != true) return;
        await RunAsync("匯入", async p => $"已匯入地圖：{await _maps.ImportFolderAsync(_inst.Id, dlg.FolderName, p)}");
    }

    async void Url_Click(object s, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (url.Length == 0) { StatusText.Text = "請先貼上網址"; return; }
        await RunAsync("下載", async p => $"已匯入地圖：{await _maps.DownloadAndImportAsync(_inst.Id, url, p)}");
        UrlBox.Clear();
    }

    async void Backup_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not WorldInfo w) return;
        await RunAsync("備份", async p =>
        {
            p.Report(($"壓縮 {w.Name}", 50));
            return $"已備份到：{await _maps.BackupAsync(_inst.Id, w)}";
        });
    }

    void Delete_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not WorldInfo w) return;
        if (MessageBox.Show(this, $"確定要永久刪除地圖「{w.Name}」？\n這個動作不能復原，建議先按「備份」。", "刪除地圖",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _maps.Delete(w); Reload(); StatusText.Text = $"已刪除：{w.Name}"; }
        catch (Exception ex) { StatusText.Text = "刪除失敗（遊戲正開著這張地圖嗎？）：" + ex.Message; }
    }

    void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_maps.SavesDir(_inst.Id));
        Process.Start(new ProcessStartInfo(_maps.SavesDir(_inst.Id)) { UseShellExecute = true });
    }

    void Refresh_Click(object s, RoutedEventArgs e) => Reload();

    async void Search_Click(object s, RoutedEventArgs e) { Hits.Clear(); _offset = 0; await LoadHitsAsync(); }
    async void More_Click(object s, RoutedEventArgs e) => await LoadHitsAsync();

    async Task LoadHitsAsync()
    {
        if (!CurseForgeSettings.HasKey) { StatusText.Text = "請先設定 CurseForge API 金鑰（主畫面「下載 → CurseForge」）"; return; }
        StatusText.Text = "搜尋中…";
        try
        {
            var hits = await _cf.SearchWorldsAsync(QueryBox.Text, McBox.Text, _offset);
            foreach (var h in hits) Hits.Add(h);
            _offset += hits.Count;
            MoreBtn.Visibility = hits.Count >= 20 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"已顯示 {Hits.Count} 個結果";
        }
        catch (Exception ex) { StatusText.Text = "搜尋失敗：" + ex.Message; }
    }

    async void Install_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not MapHit hit) return;
        await RunAsync("下載", async p =>
        {
            p.Report(("取得下載資訊", 0));
            var d = await _cf.GetWorldDownloadAsync(hit.ProjectId);
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ieml_map_{Guid.NewGuid():N}.zip");
            p.Report(($"下載 {d.FileName}", 0));
            await _cf.DownloadFileAsync(d.Url, tmp);
            var name = await _maps.ImportDownloadedAsync(_inst.Id, tmp, hit.Title, p, default);
            return $"已匯入地圖：{name}";
        });
    }
}

