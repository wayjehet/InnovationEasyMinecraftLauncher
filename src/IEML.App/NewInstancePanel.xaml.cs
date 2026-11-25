using System.IO;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// 新增實例面板（蓋在主畫面上的對話框，用 MainWindow.ShowModal 顯示）。
public partial class NewInstancePanel : UserControl
{
    readonly Action<bool> _close;
    public string InstanceName => NameBox.Text.Trim();
    public LoaderType Loader => LoaderBox.SelectedItem is LoaderType l ? l : LoaderType.Vanilla;
    public string RootPath => (RootBox.SelectedItem as InstanceRoot)?.Path ?? InstanceRoots.Active.Path;
    public string McVersion => VerBox.SelectedItem as string ?? "";

    public NewInstancePanel(string defaultName, Action<bool> close)
    {
        InitializeComponent();
        _close = close;
        NameBox.Text = defaultName;
        LoaderBox.ItemsSource = Enum.GetValues<LoaderType>();
        LoaderBox.SelectedIndex = 0;
        LoadRoots(InstanceRoots.Active.Path);
        Loaded += async (_, _) => { NameBox.Focus(); NameBox.SelectAll(); await LoadVersions(); };
    }

    void LoadRoots(string selectPath)
    {
        var all = InstanceRoots.All;
        RootBox.ItemsSource = all;
        RootBox.SelectedItem = all.FirstOrDefault(r => string.Equals(r.Path, selectPath, StringComparison.OrdinalIgnoreCase)) ?? all[0];
    }

    void AddRoot_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "選擇（或新建）一個資料夾當作實例目錄" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try { var r = InstanceRoots.Add(dlg.FolderName); LoadRoots(r.Path); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "新增實例目錄"); }
    }

    async Task LoadVersions()
    {
        try
        {
            VerBox.ItemsSource = await VersionService.ListAsync(SnapBox.IsChecked == true);
            VerBox.SelectedIndex = 0;
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "無法取得版本清單：" + ex.Message, "新增實例"); }
    }

    async void Snap_Click(object s, RoutedEventArgs e) => await LoadVersions();

    void Ok_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(InstanceName) || string.IsNullOrEmpty(McVersion)) return;
        _close(true);
    }

    void Cancel_Click(object s, RoutedEventArgs e) => _close(false);
}
