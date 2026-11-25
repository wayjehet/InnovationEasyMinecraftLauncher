using System.IO;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Services;
namespace IEML.App;

public partial class JavaPickerPanel : UserControl
{
    readonly JavaService _svc = new();
    readonly string _mc;

    /// 使用者選的路徑；null 代表「自動」（關閉結果為 true 時才有意義）。
    public string? SelectedPath { get; private set; }

    readonly Action<bool> _close;

    public JavaPickerPanel(string mcVersion, Action<bool> close)
    {
        InitializeComponent();
        _mc = mcVersion; _close = close;
        Loaded += async (_, _) => await LoadAsync(false);
    }

    async Task LoadAsync(bool refresh)
    {
        InfoText.Text = "偵測中…";
        try
        {
            var req = await _svc.GetRequiredMajorAsync(_mc);
            var list = await _svc.DetectLocalAsync(refresh);
            List.ItemsSource = list;
            var has = list.Any(j => j.Major == req);
            InfoText.Text = $"Minecraft {(_mc.Length == 0 ? "（未填版本）" : _mc)} 需要 Java {req}。" +
                            (has ? "本機有符合的版本。" : list.Any(j => j.Major > req) ? "本機沒有符合的版本，只有較高版本。" : "本機沒有可用的版本，啟動時會自動下載。");
        }
        catch (Exception ex) { InfoText.Text = "偵測失敗：" + ex.Message; }
    }

    void Cancel_Click(object s, RoutedEventArgs e) => _close(false);

    async void Refresh_Click(object s, RoutedEventArgs e) => await LoadAsync(true);

    void Auto_Click(object s, RoutedEventArgs e) { SelectedPath = null; _close(true); }

    void Pick_Click(object s, RoutedEventArgs e)
    {
        if (List.SelectedItem is not JavaInstall j) { InfoText.Text = "請先選一個，或按「自動」。"; return; }
        SelectedPath = j.Path; _close(true);
    }

    void List_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (List.SelectedItem is JavaInstall j) { SelectedPath = j.Path; _close(true); }
    }
}
