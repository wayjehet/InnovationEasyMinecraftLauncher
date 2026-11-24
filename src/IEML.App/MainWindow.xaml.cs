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
        // 素材換皮：監看素材資料夾，圖片一變動畫面立刻更新；標誌有圖就顯示圖，沒有就顯示文字 IEML
        SkinManager.Instance.Start();
        SkinManager.Instance.Changed += ApplyLogo;
        ApplyLogo();
        LoadTheme();
        Loaded += (_, _) => Motion.Float(EmptyHint);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _modalClose is not null) { _modalClose(false); e.Handled = true; } };
        mainVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Status)) ToastForStatus(mainVm.Status); };
    }

    /// 重要的狀態訊息另外用提示條顯示（頁面蓋住左欄時看不到狀態文字）。
    void ToastForStatus(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        bool err = s.StartsWith("錯誤") || s.Contains("失敗");
        bool ok = s.StartsWith("已啟動") || s.StartsWith("已新增") || s.StartsWith("目前已是最新");
        if (err || ok) ShowToast(s, err);
    }

    Action<bool>? _modalClose;

    /// 在視窗內顯示一個小對話框（取代彈出視窗）。build 收到的 close(結果) 讓內容自己關閉；Esc＝取消（false）。
    /// 已有對話框開著時，舊的會以 false 結束。回傳 Task 在關閉時完成。
    public Task<bool> ShowModal(string title, Func<Action<bool>, UIElement> build, double width = 420)
    {
        _modalClose?.Invoke(false);
        var tcs = new TaskCompletionSource<bool>();
        void Close(bool result)
        {
            if (tcs.Task.IsCompleted) return;
            if (_modalClose == (Action<bool>)Close) _modalClose = null;
            ModalLayer.Visibility = Visibility.Collapsed;
            ModalBody.Content = null;
            tcs.SetResult(result);
        }
        _modalClose = Close;
        ModalTitle.Text = title;
        ModalBox.Width = width;
        ModalBody.Content = build(Close);
        ModalLayer.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(ModalBox, 0, 160);
        return tcs.Task;
    }

    int _toastId;
    public void ShowToast(string text, bool error = false)
    {
        int id = ++_toastId;
        ToastText.Text = text;
        ToastHost.BorderBrush = error ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x5C, 0x6C))
                                      : (System.Windows.Media.Brush)FindResource("AccentBrush");
        ToastHost.Visibility = Visibility.Visible;
        Motion.Toast(ToastHost, error ? 4500 : 2200, () => { if (id == _toastId) ToastHost.Visibility = Visibility.Collapsed; });
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

    /// 啟動鍵旁的 ▾：列出目前實例記錄的伺服器，選一個就啟動並直接加入（只這一次）。
    void ServerMenu_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        var menu = new ContextMenu
        {
            PlacementTarget = btn, Placement = PlacementMode.Top,
            Background = (System.Windows.Media.Brush)FindResource("PanelBg"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("LineBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
        };
        var servers = Vm.SelectedInstance?.Servers ?? new();
        if (servers.Count == 0)
            menu.Items.Add(new MenuItem { Header = "這個實例還沒有伺服器（實例設置 → 伺服器 新增）", IsEnabled = false });
        foreach (var s in servers)
        {
            var addr = s.Address;
            var label = string.IsNullOrWhiteSpace(s.Name) || s.Name == s.Address ? s.Address : $"{s.Name}　{s.Address}";
            var mi = new MenuItem { Header = label, Foreground = menu.Foreground };
            mi.Click += (_, _) => Vm.LaunchToServerCommand.Execute(addr);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    void Card_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardItem c } el)
            Motion.CardIn(el, Vm.Cards.IndexOf(c));
    }

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

    TaskCompletionSource<(bool Saved, string? Server)>? _settingsTcs;

    /// 把實例設置頁嵌進覆蓋頁；按保存回傳 true，取消或按「← 返回」回傳 false（取消會還原修改）。
    public Task<(bool Saved, string? Server)> ShowInstanceSettingsPage(IEML.Core.Models.InstanceConfig inst, IReadOnlyList<IEML.Core.Models.Account> accounts)
    {
        _settingsTcs?.TrySetResult((false, null));
        var tcs = _settingsTcs = new TaskCompletionSource<(bool Saved, string? Server)>();
        _navKey = null;
        ShowStage("實例設置 · " + inst.Name, "基本、模組、光影、資源包與快捷卡片");
        StageScroll.Visibility = Visibility.Collapsed;
        StageHost.Visibility = Visibility.Visible;
        InstanceSettingsPage? page = null;
        page = new InstanceSettingsPage(inst, accounts, new InstanceService(), ok =>
        {
            tcs.TrySetResult((ok, ok ? page!.LaunchServer : null));
            _navKey = null;
            int tok = ++_closeToken;
            Motion.FadeOut(StageOverlay, () => { if (tok != _closeToken) return; StageOverlay.Visibility = Visibility.Collapsed; StageHost.Content = null; });
        });
        StageHost.Content = page;
        return tcs.Task;
    }

    readonly Stack<(string Title, string Footer, UIElement Page)> _pageStack = new();
    TaskCompletionSource? _pageTcs;

    /// 在覆蓋頁裡顯示一個頁面（取代彈出視窗）。回傳的 Task 在整個覆蓋頁關閉（或被別的頁面取代）時完成。
    /// push＝true：疊在目前頁面上，按「← 返回」會回到上一頁；scroll＝true：頁面太高時整頁可捲動。
    public Task ShowPage(string title, string footer, UIElement page, bool push = false, bool scroll = false)
    {
        UIElement content = scroll ? new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page } : page;
        if (push && StageHost.Visibility == Visibility.Visible && StageHost.Content is UIElement cur)
        {
            _pageStack.Push((StageTitle.Text, StageFooter.Text, cur));
            StageTitle.Text = title; StageFooter.Text = footer;
            StageHost.Content = content;
            Motion.FadeSlideIn(StageHost);
            return (_pageTcs ??= new()).Task;
        }
        ShowStage(title, footer);                 // 會結束上一個頁面的 Task、清掉返回堆疊
        _pageTcs = new();
        StageScroll.Visibility = Visibility.Collapsed;
        StageHost.Visibility = Visibility.Visible;
        StageHost.Content = content;
        return _pageTcs.Task;
    }

    // 右欄按鈕「再按一次就收回去」：記住目前覆蓋頁是哪個按鈕開的（覆蓋頁遮住左＋中欄，所以只有右欄按鈕能在頁面開著時被按到）
    string? _navKey;
    int _closeToken;

    /// 已經開著同一個按鈕的頁面 → 關掉並回傳 true（呼叫端直接 return）；否則記下這個按鈕並回傳 false。
    bool ToggleNav(string key)
    {
        if (StageOverlay.Visibility == Visibility.Visible && _navKey == key)
        {
            _pageStack.Clear();                       // 不管疊了幾層，一次全部收回
            StageBack_Click(this, new RoutedEventArgs());
            return true;
        }
        _navKey = key;
        return false;
    }

    void StageBack_Click(object sender, RoutedEventArgs e)
    {
        if (StageHost.Content is InstanceSettingsPage p) { p.Discard(); _settingsTcs?.TrySetResult((false, null)); }
        else if (_pageStack.Count > 0)
        {
            var s = _pageStack.Pop();
            StageTitle.Text = s.Title; StageFooter.Text = s.Footer;
            StageHost.Content = s.Page;
            Motion.FadeSlideIn(StageHost);
            return;
        }
        _navKey = null;
        int tok = ++_closeToken;
        Motion.FadeOut(StageOverlay, () =>
        {
            if (tok != _closeToken) return;          // 淡出途中又開了別的頁面：不要把新頁面關掉
            StageOverlay.Visibility = Visibility.Collapsed;
            StageHost.Content = null;                // 觸發頁面的 Unloaded（取消事件訂閱、停計時器）
            _pageStack.Clear();
            _pageTcs?.TrySetResult(); _pageTcs = null;
        });
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
        _closeToken++;                                // 取消還沒做完的淡出關閉
        StageActions.Children.Clear();
        _pageStack.Clear(); _pageTcs?.TrySetResult(); _pageTcs = null;
        StageHost.Content = null; StageHost.Visibility = Visibility.Collapsed; StageScroll.Visibility = Visibility.Visible;
        StageTitle.Text = title;
        StageFooter.Text = footer;
        StageOverlay.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(StageOverlay);
    }

    /// 「更多」「P2P」這種按鈕清單頁：項目依序淡入。
    void StaggerActions() => Motion.Stagger(StageActions);

    void ApplyLogo()
    {
        Logo.Source = SkinManager.Instance.Get("logo");
        LogoText.Visibility = Logo.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    void SettingsStage_Click(object sender, RoutedEventArgs e)
    {
        if (ToggleNav("settings")) return;
        ShowStage("設置", "配色、帳號、素材與啟動器資訊");
        StageActions.Children.Clear();
        StageActions.ItemWidth = double.NaN;
        StageActions.ItemHeight = double.NaN;

        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "主題配色", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,12) });
        var editor = new ThemeEditor(_theme, () => ApplyTheme());
        panel.Children.Add(Dyn(new TextBlock { Text = "強調色預設", Margin = new Thickness(0,0,0,6) }, TextBlock.ForegroundProperty, "MutedBrush"));
        var swatches = new WrapPanel { Margin = new Thickness(0,0,0,12) };
        foreach (var c in new[] { "#00FFFF", "#7CFF6B", "#FFB020", "#FF5C8A", "#8B7CFF", "#FFFFFF" })
        {
            var swatch = new Border
            {
                Width = 36, Height = 36, Margin = new Thickness(0,0,8,8), CornerRadius = new CornerRadius(6),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(c)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = c
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            // 不用 Button：全域 ArtButton 樣式的範本會忽略 Background，六個色塊會全部變成同一個青色
            swatch.MouseLeftButtonUp += (_, _) => { _theme.Accent = c; editor.Refresh(); ApplyTheme(); };
            swatches.Children.Add(swatch);
        }
        panel.Children.Add(swatches);
        panel.Children.Add(Dyn(new TextBlock { Text = "點一個部分展開，用 R、G、B 拉條調整；拖動時即時套用並自動儲存。", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,8) }, TextBlock.ForegroundProperty, "MutedBrush"));
        panel.Children.Add(editor);

        panel.Children.Add(new TextBlock { Text = "自訂圖片（素材）", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,20,0,8) });
        panel.Children.Add(new SkinEditor());

        var actions = new WrapPanel { Margin = new Thickness(0,8,0,0) };
        actions.Children.Add(new Button { Content = "帳號管理", Command = Vm.OpenAccountsCommand, Height = 36, Padding = new Thickness(12,4,12,4), Margin = new Thickness(0,0,8,8) });
        var resetButton = new Button { Content = "恢復預設配色", Height = 36, Padding = new Thickness(12,4,12,4) };
        resetButton.Click += (_, _) => { _theme.CopyFrom(new ThemeSettings()); editor.Refresh(); ApplyTheme(); };
        actions.Children.Add(resetButton);
        panel.Children.Add(actions);
        panel.Children.Add(Dyn(new TextBlock { Text = $"版本：{AppInfo.Version}\n專案：{AppInfo.Repo}", Margin = new Thickness(0,12,0,0), TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "MutedBrush"));

        StageActions.Children.Add(panel);
        StageOverlay.Visibility = Visibility.Visible;
    }

    /// 讓元素的屬性綁到主題資源（DynamicResource）：配色改變時會立刻重繪，不用重開。
    static T Dyn<T>(T el, DependencyProperty prop, string key) where T : FrameworkElement { el.SetResourceReference(prop, key); return el; }

    void DownloadStage_Click(object sender, RoutedEventArgs e)
    {
        if (ToggleNav("download")) return;
        ShowStage("下載", "建立實例、搜尋 Modrinth／CurseForge、匯入整合包與管理已安裝內容");
        var control = new DownloadStageControl(new InstanceService());
        control.InstanceCreated += () => _ = Vm.RefreshInstancesPublicAsync();
        control.Margin = new Thickness(0);
        StageActions.Children.Clear();
        StageActions.ItemWidth = double.NaN;
        StageActions.ItemHeight = double.NaN;
        StageActions.Children.Add(control);
        StageOverlay.Visibility = Visibility.Visible;
    }

    void P2PStage_Click(object sender, RoutedEventArgs e)
    {
        if (ToggleNav("p2p")) return;
        ShowStage("P2P 連線", "陶瓦聯機與 EasyTier 房間工具");
        AddStageAction("陶瓦聯機 Terracotta", "開房、加入房間與查看連線狀態", Vm.OpenTerracottaCommand);
        AddStageAction("EasyTier 房間", "進階虛擬區域網路連線", Vm.OpenRoomCommand);
        StaggerActions();
    }

    void MoreStage_Click(object sender, RoutedEventArgs e)
    {
        if (ToggleNav("more")) return;
        ShowStage("更多工具", "伺服器、遊戲日誌、種子地圖、地圖與帳號");
        // 固定 2 欄 × 3 列，順序固定，不隨視窗大小跑位；最後一格預留
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 3, Height = 3 * 84, VerticalAlignment = VerticalAlignment.Top };
        void Add(string t, string d, ICommand cmd)
        {
            var b = new Button
            {
                Margin = new Thickness(0, 0, 8, 8), Style = (Style)FindResource("GhostButton"), Command = cmd,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = t, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") },
                        new TextBlock { Text = d, Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,0) }
                    }
                }
            };
            grid.Children.Add(b);
        }
        Add("伺服器查找", "收藏伺服器並查詢即時狀態", Vm.OpenServersCommand);
        Add("遊戲日誌", "查看遊戲輸出與啟動錯誤", Vm.OpenLogCommand);
        Add("種子地圖", "結構與群系座標查詢", Vm.OpenSeedMapCommand);
        Add("地圖管理", "匯入、備份與瀏覽地圖", Vm.OpenMapsCommand);
        Add("帳號管理", "離線／Microsoft 帳號與重新命名", Vm.OpenAccountsCommand);
        grid.Children.Add(new Border());                       // 預留格
        StageScroll.Visibility = Visibility.Collapsed;
        StageHost.Visibility = Visibility.Visible;
        StageHost.Content = grid;
        Motion.Stagger(grid);
    }

    void MusicStage_Click(object sender, RoutedEventArgs e)
    {
        if (ToggleNav("music")) return;
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
}
