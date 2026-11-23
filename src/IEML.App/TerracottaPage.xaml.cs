using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using IEML.Core.Services;
namespace IEML.App;

public partial class TerracottaPage : UserControl
{
    readonly TerracottaService _svc;

    public TerracottaPage(TerracottaService svc)
    {
        InitializeComponent();
        _svc = svc;
        _svc.StateChanged += OnState;
        _svc.StatusChanged += OnStatus;
        Unloaded += (_, _) => { _svc.StateChanged -= OnState; _svc.StatusChanged -= OnStatus; };
        if (_svc.Current is not null) Show(_svc.Current);
    }

    void OnState(TerracottaState s) => Dispatcher.BeginInvoke(() => Show(s));
    void OnStatus(string s) => Dispatcher.BeginInvoke(() => InfoText.Text = s);

    static string Describe(TerracottaState s) => s.State switch
    {
        "waiting" => "待機中。可以開房或加入房間。",
        "host-scanning" => "正在掃描你的 Minecraft…請確認已進入世界並「對區域網路開放」。",
        "host-starting" => "正在建立房間…",
        "host-ok" => "房間已就緒！把房間碼給朋友。",
        "guest-connecting" => "正在連線到房主…",
        "guest-starting" => $"正在建立連線（連線難度：{s.Difficulty ?? "未知"}）…",
        "guest-ok" => "加入成功！在多人遊戲列表雙擊「陶瓦聯機大廳」，或用下方直連位址連線。",
        "exception" => s.ExceptionType switch
        {
            0 => "無法連線到房主：房間已關閉或網路不穩。",
            1 => "與房主的連線中斷。",
            2 => "你這邊的 EasyTier 崩潰了。",
            3 => "房主的 EasyTier 崩潰了。",
            4 => "房主的 Minecraft 已退出世界，房間自動關閉。",
            5 => "協定錯誤（可向陶瓦開發者回報）。",
            _ => "發生未知的錯誤。"
        } + "  請按「返回待機」。",
        "stopped" => "陶瓦聯機已結束或沒有回應，重新按開房／加入即可重新啟動。",
        _ => s.State
    };

    void Show(TerracottaState s)
    {
        StateText.Text = Describe(s);
        RoomBox.Text = s.Room ?? "";
        RoomRow.Visibility = string.IsNullOrEmpty(s.Room) ? Visibility.Collapsed : Visibility.Visible;
        UrlBox.Text = s.Url ?? "";
        UrlRow.Visibility = string.IsNullOrEmpty(s.Url) ? Visibility.Collapsed : Visibility.Visible;
        MemberList.ItemsSource = s.Profiles.Select(p => $"{p.Name}  [{p.Kind}]").ToList();
        var idle = s.State is "waiting" or "stopped";
        HostBtn.IsEnabled = JoinBtn.IsEnabled = idle;
    }

    string? Player => string.IsNullOrWhiteSpace(PlayerBox.Text) ? null : PlayerBox.Text.Trim();

    async void Host_Click(object s, RoutedEventArgs e)
    {
        try { InfoText.Text = ""; await _svc.StartHostAsync(Player); }
        catch (Exception ex) { InfoText.Text = "錯誤：" + ex.Message; }
    }

    async void Join_Click(object s, RoutedEventArgs e)
    {
        try { InfoText.Text = ""; await _svc.StartGuestAsync(CodeBox.Text, Player); }
        catch (Exception ex) { InfoText.Text = "錯誤：" + ex.Message; }
    }

    async void Reset_Click(object s, RoutedEventArgs e)
    {
        try { await _svc.ResetAsync(); }
        catch (Exception ex) { InfoText.Text = "錯誤：" + ex.Message; }
    }

    void CopyRoom_Click(object s, RoutedEventArgs e) => Copy(RoomBox.Text);
    void CopyUrl_Click(object s, RoutedEventArgs e) => Copy(UrlBox.Text);

    void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); InfoText.Text = "已複製"; }
        catch (Exception ex) { InfoText.Text = "複製失敗：" + ex.Message; }
    }

    void Link_Navigate(object s, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        e.Handled = true;
    }
}
