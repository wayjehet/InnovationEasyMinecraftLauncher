using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

public partial class CurseForgePage : UserControl
{
    readonly CurseForgeService _svc;
    readonly InstanceConfig? _inst;
    bool _busy;
    public ObservableCollection<ContentHit> Results { get; } = new();

    public CurseForgePage(IInstanceService instances, InstanceConfig? inst)
    {
        _svc = new CurseForgeService(instances);
        _inst = inst;
        InitializeComponent(); DataContext = this;
        Info.Text = inst is null
            ? "未選擇實例：只能安裝整合包（模組／光影／資源包需要先在主畫面選一個實例）"
            : $"模組／光影／資源包會安裝到：{inst.Name}（{inst.McVersion} / {inst.Loader}）；整合包一律建立新實例";
        StatusText.Text = CurseForgeSettings.SourceText + (CurseForgeSettings.HasKey ? "，可以搜尋" : "（匯入 .mrpack 不需要金鑰）");
    }

    ContentKind Kind => Enum.Parse<ContentKind>((string)((ComboBoxItem)KindBox.SelectedItem).Tag);

    void SaveKey_Click(object s, RoutedEventArgs e)
    {
        try
        {
            CurseForgeSettings.SetKey(KeyBox.Password);
            KeyBox.Clear();
            StatusText.Text = CurseForgeSettings.HasUserKey ? "金鑰已儲存" : "已清除你的金鑰。" + CurseForgeSettings.SourceText;
        }
        catch (Exception ex) { StatusText.Text = "儲存失敗：" + ex.Message; }
    }

    async void Search_Click(object s, RoutedEventArgs e)
    {
        if (!CurseForgeSettings.HasKey) { StatusText.Text = "請先填入並儲存 CurseForge API 金鑰"; return; }
        var kind = Kind;
        if (_inst is null && kind != ContentKind.Modpack) { StatusText.Text = "請先在主畫面選一個實例"; return; }
        StatusText.Text = "搜尋中…";
        try
        {
            var hits = await _svc.SearchAsync(QueryBox.Text, kind, kind == ContentKind.Modpack ? "" : _inst!.McVersion,
                kind == ContentKind.Modpack ? null : _inst!.Loader);
            Results.Clear(); foreach (var h in hits) Results.Add(h);
            StatusText.Text = $"找到 {hits.Count} 個結果";
        }
        catch (Exception ex) { StatusText.Text = "搜尋失敗：" + ex.Message; }
    }

    IProgress<(string Stage, double Percent)> MakeProgress(string title) =>
        new Progress<(string Stage, double Percent)>(p => { StatusText.Text = $"{title}：{p.Stage}"; Bar.Value = Math.Clamp(p.Percent, 0, 100); });

    void ShowManual(CfInstallResult r)
    {
        if (r.ManualFiles.Count == 0) return;
        _ = Dialogs.InfoAsync("需要手動下載",
            $"有 {r.ManualFiles.Count} 個檔案無法自動安裝（作者不開放第三方下載或下載失敗），清單已存到實例資料夾的 manual-downloads.txt：\n\n" +
            string.Join("\n", r.ManualFiles.Take(12)) + (r.ManualFiles.Count > 12 ? "\n…" : ""));
    }

    async void Install_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not ContentHit hit) return;
        if (_busy) { StatusText.Text = "已有項目正在安裝中"; return; }
        _busy = true; Bar.Value = 0; Bar.Visibility = Visibility.Visible;
        try
        {
            var prog = MakeProgress(hit.Title);
            if (hit.Kind == ContentKind.Modpack)
            {
                var r = await _svc.InstallModpackAsync(hit.ProjectId, prog);
                StatusText.Text = $"已建立新實例：{r.PackName}（安裝 {r.Installed} 個檔案）";
                ShowManual(r);
            }
            else
            {
                if (_inst is null) { StatusText.Text = "請先在主畫面選一個實例"; return; }
                var r = await _svc.InstallFileAsync(_inst.Id, hit.ProjectId, hit.Kind, prog);
                StatusText.Text = $"已安裝：{hit.Title}（{string.Join("、", r.Files)}）";
            }
        }
        catch (Exception ex) { StatusText.Text = "安裝失敗：" + ex.Message; }
        finally { _busy = false; Bar.Visibility = Visibility.Collapsed; }
    }

    async void Import_Click(object s, RoutedEventArgs e)
    {
        if (_busy) { StatusText.Text = "已有項目正在安裝中"; return; }
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇整合包",
            Filter = "整合包 (*.zip;*.mrpack)|*.zip;*.mrpack|所有檔案 (*.*)|*.*"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        _busy = true; Bar.Value = 0; Bar.Visibility = Visibility.Visible;
        try
        {
            var r = await _svc.ImportLocalAsync(dlg.FileName, MakeProgress(System.IO.Path.GetFileName(dlg.FileName)));
            StatusText.Text = $"已建立新實例：{r.PackName}" + (r.Installed > 0 ? $"（安裝 {r.Installed} 個檔案）" : "");
            ShowManual(r);
        }
        catch (Exception ex) { StatusText.Text = "匯入失敗：" + ex.Message; }
        finally { _busy = false; Bar.Visibility = Visibility.Collapsed; }
    }
}
