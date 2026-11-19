using System.Diagnostics;
using System.Windows;
using IEML.Core.Services;
namespace IEML.App;

public partial class LauncherSettingsWindow : Window
{
    static readonly string[] AssetNames =
    {
        "background.png", "logo.png", "avatar_frame.png", "avatar_default.png", "btn_launch.png",
        "btn_instance_settings.png", "btn_instance_pick.png", "card.png",
        "btn_settings.png", "btn_download.png", "btn_p2p.png", "btn_more.png", "btn_music.png"
    };

    public LauncherSettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        KeyStatus.Text = CurseForgeSettings.SourceText;
        VersionText.Text = $"Innovation Easy Minecraft Launcher  Beta {AppInfo.Version}";
        AssetList.Text = $"資料夾：{ArtAssets.UserDir}\n\n" +
            string.Join("\n", AssetNames.Select(n => (ArtAssets.Find(n) is null ? "○ " : "● ") + n)) +
            "\n\n●＝已找到　○＝使用預設樣式";
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

    void OpenAssets_Click(object s, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ArtAssets.UserDir);
            Process.Start(new ProcessStartInfo(ArtAssets.UserDir) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "無法開啟資料夾"); }
    }

    void OpenRepo_Click(object s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.Repo) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "無法開啟網頁"); }
    }
}
