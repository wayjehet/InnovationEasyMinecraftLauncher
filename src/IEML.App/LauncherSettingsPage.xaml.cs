using System.IO;
using System.Diagnostics;
using System.Windows;
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
        VersionText.Text = $"Innovation Easy Minecraft Launcher  Beta {AppInfo.Version}";
        SkinHost.Content = new SkinEditor();
    }

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
