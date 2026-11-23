using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Services;
namespace IEML.App;

public partial class ServerPage : UserControl
{
    readonly ServerPingService _svc = new();
    public ObservableCollection<string> Saved { get; } = new();

    public ServerPage()
    {
        InitializeComponent(); DataContext = this;
        Loaded += async (_, _) => await RefreshAsync();
    }

    static string Fmt(ServerStatus s) => s.Online
        ? $"● {s.Address}  {s.Players}/{s.MaxPlayers}  {s.LatencyMs}ms  [{s.Version}]  {s.Motd.Replace('\n', ' ')}"
        : $"○ {s.Address}  離線或無法連線";

    async void Ping_Click(object s, RoutedEventArgs e)
    {
        Result.Text = "查詢中…";
        Result.Text = Fmt(await _svc.PingAsync(AddrBox.Text));
    }

    async void Fav_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddrBox.Text)) return;
        await _svc.SaveFavoriteAsync(AddrBox.Text.Trim());
        await RefreshAsync();
    }

    void Browse_Click(object s, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowPage("公開伺服器", "瀏覽公開的 Minecraft 伺服器", new PublicServerPage(), push: true);

    async void Refresh_Click(object s, RoutedEventArgs e) => await RefreshAsync();

    async Task RefreshAsync()
    {
        Saved.Clear();
        foreach (var r in await _svc.SearchAsync("")) Saved.Add(Fmt(r));
    }
}
