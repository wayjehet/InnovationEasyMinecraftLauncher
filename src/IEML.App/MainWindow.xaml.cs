using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace IEML.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        // 標誌：有 logo.png 就顯示圖，沒有就顯示文字 IEML
        Logo.Source = ArtAssets.Load("logo.png");
        LogoText.Visibility = Logo.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// 右欄「下載」「更多」：按下去彈出選單（選單項目的指令綁定到 MainViewModel）
    void OpenMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.ContextMenu is null) return;
        b.ContextMenu.DataContext = DataContext;
        b.ContextMenu.PlacementTarget = b;
        b.ContextMenu.Placement = PlacementMode.Left;
        b.ContextMenu.IsOpen = true;
    }

    void Music_Click(object sender, RoutedEventArgs e) => MusicPopup.IsOpen = true;

    // ---------- 快捷啟動卡片：拖拉 ----------
    CardItem? _dragCard;
    Point _dragStart, _cardStart;
    bool _dragging;
    int _zTop;

    MainViewModel Vm => (MainViewModel)DataContext;

    void CardsHost_SizeChanged(object sender, SizeChangedEventArgs e) => Vm.SetCardsArea(CardsHost.ActualWidth, CardsHost.ActualHeight);

    void Card_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CardItem c }) return;
        _dragCard = c; _dragging = false;
        c.Z = ++_zTop;                                  // 按下就拉到最上層
        _dragStart = e.GetPosition(CardsHost);          // 相對於固定不動的畫布，卡片移動時不會回授
        _cardStart = new Point(c.X, c.Y);
    }

    void Card_Move(object sender, MouseEventArgs e)
    {
        if (_dragCard is null || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement fe) return;
        var p = e.GetPosition(CardsHost);
        double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        if (!_dragging && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
                       && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        _dragCard.X = Math.Clamp(_cardStart.X + dx, 0, Math.Max(0, CardsHost.ActualWidth - fe.ActualWidth));
        _dragCard.Y = Math.Clamp(_cardStart.Y + dy, 0, Math.Max(0, CardsHost.ActualHeight - fe.ActualHeight));
    }

    async void Card_Up(object sender, MouseButtonEventArgs e)
    {
        var c = _dragCard; var moved = _dragging;
        _dragCard = null; _dragging = false;
        if (c is null) return;
        if (!moved) { c.LaunchCommand.Execute(null); return; }   // 沒拖動＝點一下，啟動
        try { await Vm.SaveCardPositionAsync(c); } catch { }
    }
}
