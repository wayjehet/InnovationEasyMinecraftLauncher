using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Services;
namespace IEML.App;

public partial class RoomPage : UserControl
{
    readonly EasyTierService _svc;
    bool _busy;

    public RoomPage(EasyTierService svc)
    {
        InitializeComponent();
        _svc = svc;
        _svc.StatusChanged += OnStatus;
        Unloaded += (_, _) => _svc.StatusChanged -= OnStatus;
        Refresh();
    }

    void OnStatus(string s) => Dispatcher.BeginInvoke(() => StatusText.Text = s);

    void Refresh()
    {
        var room = _svc.CurrentRoom;
        NameBox.Text = room?.NetworkName ?? "";
        SecretBox.Text = room?.Secret ?? "";
        IpBox.Text = _svc.VirtualIp ?? "";
        InviteBox.Text = room is null ? "" : EasyTierService.ToInviteCode(room);
        var running = _svc.IsRunning;
        CreateBtn.IsEnabled = JoinBtn.IsEnabled = !_busy && !running;
        StopBtn.IsEnabled = running || _busy;
    }

    async Task RunAsync(EasyTierRoom room)
    {
        _busy = true; Refresh();
        try { await _svc.StartAsync(room); }
        catch (Exception ex) { StatusText.Text = "錯誤：" + ex.Message; }
        finally { _busy = false; Refresh(); }
    }

    async void Create_Click(object s, RoutedEventArgs e) => await RunAsync(_svc.CreateRoom());

    async void Join_Click(object s, RoutedEventArgs e)
    {
        if (!EasyTierService.TryParseInviteCode(JoinBox.Text, out var room))
        {
            StatusText.Text = "邀請碼格式不正確，應為「房號:密碼」。";
            return;
        }
        await RunAsync(room);
    }

    async void Stop_Click(object s, RoutedEventArgs e)
    {
        await _svc.StopAsync();
        Refresh();
    }

    void Copy_Click(object s, RoutedEventArgs e)
    {
        var text = (s as Button)?.Tag switch
        {
            "Name" => NameBox.Text,
            "Secret" => SecretBox.Text,
            "Ip" => IpBox.Text,
            "Invite" => InviteBox.Text,
            _ => ""
        };
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); StatusText.Text = "已複製"; }
        catch (Exception ex) { StatusText.Text = "複製失敗：" + ex.Message; }
    }
}
