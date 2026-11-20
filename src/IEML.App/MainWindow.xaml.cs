using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IEML.Core.Services;

namespace IEML.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var mainVm = new MainViewModel();
        DataContext = mainVm;
        Loaded += async (_, _) => { UpdateService.CleanupOld(); try { await mainVm.AutoCheckUpdateAsync(); } catch { } };   // 啟動時背景檢查更新
        // 標誌：有 logo.png 就顯示圖，沒有就顯示文字 IEML
        Logo.Source = ArtAssets.Load("logo.png");
        LogoText.Visibility = Logo.Source is null ? Visibility.Visible : Visibility.Collapsed;
        LoadTheme();
    }

    protected override void OnClosed(EventArgs e)
    {
        try { Vm.Shutdown(); } catch { }
        base.OnClosed(e);
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
        (sender as UIElement)?.CaptureMouse();          // 拖快一點滑鼠離開拖曳區也不會斷
    }

    void Card_Move(object sender, MouseEventArgs e)
    {
        if (_dragCard is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(CardsHost);
        double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        if (!_dragging && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
                       && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        _dragCard.X = Math.Clamp(_cardStart.X + dx, 0, Math.Max(0, CardsHost.ActualWidth - MainViewModel.CardWidth));
        _dragCard.Y = Math.Clamp(_cardStart.Y + dy, 0, Math.Max(0, CardsHost.ActualHeight - MainViewModel.CardHeight));
    }

    async void Card_Up(object sender, MouseButtonEventArgs e)
    {
        var c = _dragCard; var moved = _dragging;
        _dragCard = null; _dragging = false;
        (sender as UIElement)?.ReleaseMouseCapture();
        if (c is null || !moved) return;                         // 啟動／設置改由卡片上的按鈕負責
        try { await Vm.SaveCardPositionAsync(c); } catch { }
    }

    void StageBack_Click(object sender, RoutedEventArgs e)
    {
        StageOverlay.Visibility = Visibility.Collapsed;
    }

    void AddStageAction(string title, string description, ICommand command)
    {
        var card = new Button
        {
            Margin = new Thickness(0, 0, 8, 8),
            Style = (Style)FindResource("GhostButton"),
            Command = command,
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") },
                    new TextBlock { Text = description, Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,0) }
                }
            }
        };
        StageActions.Children.Add(card);
    }

    void ShowStage(string title, string footer)
    {
        StageActions.Children.Clear();
        StageTitle.Text = title;
        StageFooter.Text = footer;
        StageOverlay.Visibility = Visibility.Visible;
    }

    void SettingsStage_Click(object sender, RoutedEventArgs e)
    {
        ShowStage("設置", "配色、帳號、素材與啟動器資訊");
        StageActions.Children.Clear();
        StageActions.ItemWidth = double.NaN;
        StageActions.ItemHeight = double.NaN;

        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "主題配色", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,12) });
        var colors = new[] { "#00FFFF", "#7CFF6B", "#FFB020", "#FF5C8A", "#8B7CFF", "#FFFFFF" };
        var swatches = new WrapPanel { Margin = new Thickness(0,0,0,12) };
        foreach (var c in colors)
        {
            var swatch = new Button
            {
                Width = 36, Height = 36, Margin = new Thickness(0,0,8,8),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(c)),
                BorderBrush = (System.Windows.Media.Brush)FindResource("LineBrush"),
                BorderThickness = new Thickness(1)
            };
            swatch.Click += (_, _) => ApplyTheme(c, "#888888", "#333333");
            swatches.Children.Add(swatch);
        }
        panel.Children.Add(swatches);
        AddColorControl(panel, "強調色", "accent", "#00FFFF");
        AddColorControl(panel, "圖標／次要文字", "icon", "#888888");
        AddColorControl(panel, "外框與分隔線", "line", "#333333");

        var actions = new WrapPanel { Margin = new Thickness(0,8,0,0) };
        actions.Children.Add(new Button { Content = "帳號管理", Command = Vm.OpenAccountsCommand, Height = 36, Padding = new Thickness(12,4,12,4), Margin = new Thickness(0,0,8,8) });
        actions.Children.Add(new Button { Content = "開啟素材資料夾", Height = 36, Padding = new Thickness(12,4,12,4), Margin = new Thickness(0,0,8,8) });
        ((Button)actions.Children[1]).Click += (_, _) =>
        {
            System.IO.Directory.CreateDirectory(ArtAssets.UserDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ArtAssets.UserDir) { UseShellExecute = true });
        };
        var resetButton = new Button { Content = "恢復預設配色", Height = 36, Padding = new Thickness(12,4,12,4) };
        resetButton.Click += (_, _) => ApplyTheme("#00FFFF", "#888888", "#333333");
        actions.Children.Add(resetButton);
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = $"版本：{AppInfo.Version}\n專案：{AppInfo.Repo}", Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), Margin = new Thickness(0,12,0,0), TextWrapping = TextWrapping.Wrap });

        StageActions.Children.Add(panel);
        StageOverlay.Visibility = Visibility.Visible;
    }

    void DownloadStage_Click(object sender, RoutedEventArgs e)
    {
        ShowStage("下載", "建立實例、搜尋 Modrinth／CurseForge、匯入整合包與管理已安裝內容");
        var control = new DownloadStageControl(new InstanceService());
        control.Margin = new Thickness(0);
        StageActions.Children.Clear();
        StageActions.ItemWidth = double.NaN;
        StageActions.ItemHeight = double.NaN;
        StageActions.Children.Add(control);
        StageOverlay.Visibility = Visibility.Visible;
    }

    void P2PStage_Click(object sender, RoutedEventArgs e)
    {
        ShowStage("P2P 連線", "陶瓦聯機與 EasyTier 房間工具");
        AddStageAction("陶瓦聯機 Terracotta", "開房、加入房間與查看連線狀態", Vm.OpenTerracottaCommand);
        AddStageAction("EasyTier 房間", "進階虛擬區域網路連線", Vm.OpenRoomCommand);
    }

    void MoreStage_Click(object sender, RoutedEventArgs e)
    {
        ShowStage("更多工具", "伺服器、遊戲日誌、種子地圖、地圖與帳號");
        AddStageAction("伺服器查找", "收藏伺服器並查詢即時狀態", Vm.OpenServersCommand);
        AddStageAction("遊戲日誌", "查看遊戲輸出與啟動錯誤", Vm.OpenLogCommand);
        AddStageAction("種子地圖", "結構與群系座標查詢", Vm.OpenSeedMapCommand);
        AddStageAction("地圖管理", "匯入、備份與瀏覽地圖", Vm.OpenMapsCommand);
        AddStageAction("帳號管理", "離線／Microsoft 帳號與重新命名", Vm.OpenAccountsCommand);
    }

    void MusicStage_Click(object sender, RoutedEventArgs e)
    {
        ShowStage("音樂", "播放、暫停、切換曲目或選擇音樂資料夾");
        StageActions.Children.Clear();
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = Vm.MusicTitle, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,12) });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) };
        controls.Children.Add(new Button { Content = "⏮", Width = 44, Height = 36, Command = Vm.PrevMusicCommand, Margin = new Thickness(0,0,6,0) });
        controls.Children.Add(new Button { Content = "▶", Width = 44, Height = 36, Command = Vm.PlayMusicCommand, Margin = new Thickness(0,0,6,0) });
        controls.Children.Add(new Button { Content = "⏸", Width = 44, Height = 36, Command = Vm.PauseMusicCommand, Margin = new Thickness(0,0,6,0) });
        controls.Children.Add(new Button { Content = "⏭", Width = 44, Height = 36, Command = Vm.NextMusicCommand });
        panel.Children.Add(controls);
        panel.Children.Add(new Slider { Minimum = 0, Maximum = 1, Value = Vm.MusicVolume, Margin = new Thickness(0,0,0,12) });
        panel.Children.Add(new Button { Content = "📁 音樂資料夾", Command = Vm.AddMusicFolderCommand, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12,6,12,6) });
        StageActions.ItemWidth = double.NaN;
        StageActions.ItemHeight = double.NaN;
        StageActions.Children.Add(panel);
        StageOverlay.Visibility = Visibility.Visible;
    }

    static readonly string ThemePath = System.IO.Path.Combine(IEML.Core.Services.AppPaths.Root, "theme.json");

    string _themeAccent = "#00FFFF";
    string _themeIcon = "#888888";
    string _themeLine = "#333333";

    void ApplyTheme(string accent, string icon, string line)
    {
        _themeAccent = accent;
        _themeIcon = icon;
        _themeLine = line;
        var appRes = Application.Current.Resources;
        appRes["AccentBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent));
        appRes["TextBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        appRes["LineBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(line));
        appRes["MutedBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(icon));
        _ = System.IO.File.WriteAllTextAsync(ThemePath, System.Text.Json.JsonSerializer.Serialize(new { Accent = accent, Icon = icon, Line = line }));
    }

    void AddColorControl(StackPanel panel, string title, string key, string defaultValue)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 8, 12) };
        row.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 4), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        var picker = new Microsoft.Win32.SaveFileDialog();
        var box = new TextBox { Text = defaultValue, Width = 150, Height = 32 };
        box.TextChanged += (_, _) =>
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(box.Text) is System.Windows.Media.Color)
            {
                var accent = key == "accent" ? box.Text : _themeAccent;
                var icon = key == "icon" ? box.Text : _themeIcon;
                var line = key == "line" ? box.Text : _themeLine;
                ApplyTheme(accent, icon, line);
            }
        };
        row.Children.Add(box);
        panel.Children.Add(row);
    }

    void LoadTheme()
    {
        try
        {
            if (!System.IO.File.Exists(ThemePath)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(ThemePath));
            var root = doc.RootElement;
            ApplyTheme(root.TryGetProperty("Accent", out var a) ? a.GetString() ?? "#00FFFF" : "#00FFFF",
                       root.TryGetProperty("Icon", out var i) ? i.GetString() ?? "#888888" : "#888888",
                       root.TryGetProperty("Line", out var l) ? l.GetString() ?? "#333333" : "#333333");
        }
        catch { }
    }

    class ThemeData
    {
        public required string Accent { get; set; }
        public required string Icon { get; set; }
        public required string Line { get; set; }
    }
}
