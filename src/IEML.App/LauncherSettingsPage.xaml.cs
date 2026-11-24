using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Services;
namespace IEML.App;

public partial class LauncherSettingsPage : UserControl
{
    public LauncherSettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        KeyStatus.Text = CurseForgeSettings.SourceText;
        AutoUpdateBox.IsChecked = UpdateSettings.AutoCheck;
        VersionText.Text = $"Innovation Easy Minecraft Launcher  {AppInfo.Version}";
        SkinHost.Content = new SkinEditor();
        BuildRoots();
    }

    void BuildRoots()
    {
        RootsHost.Children.Clear();
        var active = InstanceRoots.Active;
        foreach (var r in InstanceRoots.All)
        {
            bool isActive = string.Equals(r.Path, active.Path, StringComparison.OrdinalIgnoreCase);
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(btns, Dock.Right);
            if (!isActive)
            {
                var use = new Button { Content = "設為使用中", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
                use.Click += (_, _) => { InstanceRoots.SetActive(r.Path); BuildRoots(); };
                btns.Children.Add(use);
            }
            var open = new Button { Content = "開啟", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
            open.Click += (_, _) =>
            {
                try { Directory.CreateDirectory(r.Path); Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true }); }
                catch (Exception ex) { _ = Dialogs.InfoAsync("無法開啟資料夾", ex.Message); }
            };
            btns.Children.Add(open);
            if (!InstanceRoots.IsDefault(r))
            {
                var rm = new Button { Content = "移除", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
                rm.Click += async (_, _) =>
                {
                    if (!await Dialogs.ConfirmAsync("移除實例目錄", $"要從清單移除「{r.Name}」嗎？\n這個目錄裡的實例會從清單消失，但檔案不會被刪除；之後可以再把同一個資料夾添加回來。", "移除", "取消")) return;
                    try { InstanceRoots.Remove(r.Path); BuildRoots(); await RefreshInstancesAsync(); }
                    catch (Exception ex) { await Dialogs.InfoAsync("無法移除", ex.Message); }
                };
                btns.Children.Add(rm);
            }
            row.Children.Add(btns);

            var text = new StackPanel();
            var title = new TextBlock { Text = r.Name + (isActive ? "　● 使用中" : ""), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var path = new TextBlock { Text = r.Path, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = r.Path };
            path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            text.Children.Add(title); text.Children.Add(path);
            row.Children.Add(text);
            RootsHost.Children.Add(row);
        }
    }

    async void AddRoot_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "選擇（或新建）一個資料夾當作實例目錄" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try { InstanceRoots.Add(dlg.FolderName); BuildRoots(); await RefreshInstancesAsync(); }
        catch (Exception ex) { await Dialogs.InfoAsync("無法新增目錄", ex.Message); }
    }

    Task RefreshInstancesAsync() => DataContext is MainViewModel vm ? vm.RefreshInstancesPublicAsync() : Task.CompletedTask;

    void SaveKey_Click(object s, RoutedEventArgs e)
    {
        try
        {
            CurseForgeSettings.SetKey(KeyBox.Password);
            KeyBox.Clear();
            KeyStatus.Text = CurseForgeSettings.HasUserKey ? "金鑰已儲存" : "已清除你的金鑰。" + CurseForgeSettings.SourceText;
        }
        catch (Exception ex) { KeyStatus.Text = "儲存失敗：" + ex.Message; }
    }

    void AutoUpdate_Click(object s, RoutedEventArgs e) => UpdateSettings.AutoCheck = AutoUpdateBox.IsChecked == true;

    void OpenRepo_Click(object s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.Repo) { UseShellExecute = true }); }
        catch (Exception ex) { _ = Dialogs.InfoAsync("無法開啟網頁", ex.Message); }
    }
}
