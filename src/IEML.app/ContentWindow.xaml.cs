using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.App;

public partial class ContentWindow : Window
{
    readonly IModrinthService _svc;
    readonly InstanceConfig? _inst;
    public ObservableCollection<ContentHit> Results { get; } = new();

    public ContentWindow(InstanceConfig? inst, IModrinthService svc)
    {
        _inst = inst; _svc = svc;
        InitializeComponent(); DataContext = this;
        Info.Text = inst is null ? "未選擇實例（只能安裝整合包）" : $"安裝到：{inst.Name}（{inst.McVersion} / {inst.Loader}）";
    }

    ContentKind Kind => Enum.Parse<ContentKind>((string)((ComboBoxItem)KindBox.SelectedItem).Tag);

    async void Search_Click(object s, RoutedEventArgs e)
    {
        var kind = Kind;
        if (_inst is null && kind != ContentKind.Modpack) { StatusText.Text = "請先在主畫面選一個實例"; return; }
        StatusText.Text = "搜尋中…";
        try
        {
            var mc = kind == ContentKind.Modpack ? "" : _inst!.McVersion;
            LoaderType? ld = kind == ContentKind.Modpack ? null : _inst!.Loader;
            var hits = await _svc.SearchAsync(QueryBox.Text, kind, mc, ld);
            Results.Clear(); foreach (var h in hits) Results.Add(h);
            StatusText.Text = $"找到 {hits.Count} 個結果";
        }
        catch (Exception ex) { StatusText.Text = "搜尋失敗：" + ex.Message; }
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
}
