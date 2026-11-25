using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IEML.Core.Models;
namespace IEML.App;

/// 像素小圖：8×8 字元圖＋調色盤 → 凍結的 BitmapSource（沒有在調色盤裡的字元＝透明）。
static class PixelArt
{
    public static BitmapSource Make(string[] rows, Dictionary<char, uint> pal)
    {
        var px = new byte[8 * 8 * 4];
        for (int y = 0; y < 8 && y < rows.Length; y++)
            for (int x = 0; x < 8 && x < rows[y].Length; x++)
            {
                if (!pal.TryGetValue(rows[y][x], out var c)) continue;
                int i = (y * 8 + x) * 4;
                px[i] = (byte)(c & 0xFF); px[i + 1] = (byte)(c >> 8 & 0xFF); px[i + 2] = (byte)(c >> 16 & 0xFF); px[i + 3] = (byte)(c >> 24);
            }
        var bmp = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, px, 8 * 4);
        bmp.Freeze();
        return bmp;
    }

    public static readonly BitmapSource Floor = Make(new[]
    { "aaaaaaaa", "aaabaaaa", "aaaaaaaa", "aaaaaaba", "aaaaaaaa", "abaaaaaa", "aaaaabaa", "aaaaaaaa" },
        new() { ['a'] = 0xFF2B2B31, ['b'] = 0xFF32323A });
    public static readonly BitmapSource Wall = Make(new[]
    { "wwwwwwwd", "wwwwwwwd", "dddddddd", "wwdwwwww", "wwdwwwww", "dddddddd", "wwwwdwww", "wwwwdwww" },
        new() { ['w'] = 0xFF666670, ['d'] = 0xFF45454E });
    public static readonly BitmapSource Stairs = Make(new[]
    { "gggggggg", "gDDDDDDg", "gDhhhhDg", "gDhKKhDg", "gDhKKhDg", "gDhhhhDg", "gDDDDDDg", "gggggggg" },
        new() { ['g'] = 0xFF6E6E76, ['D'] = 0xFF1A1A1E, ['h'] = 0xFF3A3A44, ['K'] = 0xFF000000 });

    public static readonly BitmapSource Player = Make(new[]
    { "..BBBB..", ".BBBBBB.", ".SESSES.", ".SSSSSS.", "..TTTT..", ".TTTTTT.", ".TTTTTT.", "..PP.PP." },
        new() { ['B'] = 0xFF5A3A1E, ['S'] = 0xFFC99A6B, ['E'] = 0xFF3A5FCD, ['T'] = 0xFF2FA3A3, ['P'] = 0xFF3B4FA0 });
    public static readonly BitmapSource Zombie = Make(new[]
    { "..GGGG..", ".GGGGGG.", ".GKGGKG.", ".GGGGGG.", "GGTTTTGG", "GGTTTTGG", "..TTTT..", "..DD.DD." },
        new() { ['G'] = 0xFF4E8C3A, ['D'] = 0xFF2F5A24, ['K'] = 0xFF101010, ['T'] = 0xFF2B7F8F });
    public static readonly BitmapSource Skeleton = Make(new[]
    { "..WWWW..", ".WWWWWW.", ".WKWWKW.", "..WKKW..", "...WW...", "..WWWW..", "...WW...", "..W..W.." },
        new() { ['W'] = 0xFFE6E6E6, ['K'] = 0xFF1A1A1A });
    public static readonly BitmapSource Creeper = Make(new[]
    { "GGDGGDGG", "GKKGGKKG", "GKKGGKKG", "GGGKKGGG", "GGKKKKGG", "GGKGGKGG", "GGGGGGGG", "GG.GG.GG" },
        new() { ['G'] = 0xFF4CAF50, ['D'] = 0xFF3A8A3E, ['K'] = 0xFF0F2410 });
    public static readonly BitmapSource Boss = Make(new[]
    { "Y.YYYY.Y", ".YYYYYY.", ".kRkkRk.", ".kkkkkk.", "ggkkkkgg", "g.kggk.g", "..kkkk..", "..k..k.." },
        new() { ['Y'] = 0xFFFFD23F, ['k'] = 0xFF2A2A2E, ['R'] = 0xFFFF3B3B, ['g'] = 0xFF55555F });

    public static readonly BitmapSource Potion = Make(new[]
    { "...nn...", "...gg...", "..gRRg..", ".gRRRRg.", ".gRRRRg.", ".gRRRRg.", "..gggg..", "........" },
        new() { ['n'] = 0xFFB8905A, ['g'] = 0xFFC9D6DF, ['R'] = 0xFFE04040 });
    public static readonly BitmapSource Sword = Make(new[]
    { ".......W", "......WW", ".....WW.", "W...WW..", ".WWWW...", "..YW....", ".BY.....", "B......." },
        new() { ['W'] = 0xFFE0E6EE, ['Y'] = 0xFFFFD23F, ['B'] = 0xFF7A4B22 });
    public static readonly BitmapSource Armor = Make(new[]
    { "II....II", "IIIIIIII", "IIIggIII", ".IIIIII.", ".IIggII.", ".IIIIII.", ".IIIIII.", "..IIII.." },
        new() { ['I'] = 0xFFCFD4DA, ['g'] = 0xFF8A9099 });
    public static readonly BitmapSource Apple = Make(new[]
    { "....L...", "...LL...", "..YYYY..", ".YYYYYY.", ".YYYYoY.", ".YYYYYY.", "..YYYY..", "........" },
        new() { ['Y'] = 0xFFFFD23F, ['o'] = 0xFFE0A020, ['L'] = 0xFF3FA34D });
}

/// 畫場地、敵人、子彈與特效；每一幀由 RoguelikePage 呼叫 Invalidate。
class RoguelikeView : FrameworkElement
{
    public RoguelikeGame Game { get; set; }
    /// 正版玩家的皮膚頭像；null＝用預設的像素小人。
    public BitmapSource? PlayerSprite { get; set; }

    static readonly Pen XPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(255, 70, 70)), 2));
    static readonly Pen ArrowPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(240, 220, 150)), 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Triangle });
    static readonly Pen CritPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(255, 215, 70)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Triangle });
    static readonly Brush GemBrush = Freeze(new SolidColorBrush(Color.FromRgb(60, 220, 110)));
    static readonly Brush EnemyBullet = Freeze(new SolidColorBrush(Color.FromRgb(255, 90, 90)));
    static readonly Brush BossBullet = Freeze(new SolidColorBrush(Color.FromRgb(190, 90, 255)));
    static readonly Brush FlashBrush = Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));
    static readonly Brush HurtBrush = Freeze(new SolidColorBrush(Color.FromArgb(60, 255, 0, 0)));
    static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)));

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public RoguelikeView(RoguelikeGame g)
    {
        Game = g;
        Width = RoguelikeGame.W; Height = RoguelikeGame.H;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SnapsToDevicePixels = true;
    }
    public void Invalidate() => InvalidateVisual();

    static BitmapSource EnemySprite(EKind k) => k switch
    {
        EKind.Zombie => PixelArt.Zombie, EKind.Skeleton => PixelArt.Skeleton, EKind.Creeper => PixelArt.Creeper, _ => PixelArt.Boss
    };

    protected override void OnRender(DrawingContext dc)
    {
        var g = Game;
        const double W = RoguelikeGame.W, H = RoguelikeGame.H;

        // 地板：32px 的方塊鋪滿
        for (double x = 0; x < W; x += 32)
            for (double y = 0; y < H; y += 32)
                dc.DrawImage(PixelArt.Floor, new Rect(x, y, 32, 32));

        // 生怪預告：紅色 X（越接近出生越密集閃爍）
        foreach (var m in g.Marks)
        {
            if (((int)(m.T * 10)) % 2 == 0) continue;
            double s = m.Kind == EKind.Boss ? 16 : 7;
            dc.DrawLine(XPen, new Point(m.X - s, m.Y - s), new Point(m.X + s, m.Y + s));
            dc.DrawLine(XPen, new Point(m.X - s, m.Y + s), new Point(m.X + s, m.Y - s));
        }

        foreach (var gem in g.Gems) dc.DrawEllipse(GemBrush, null, new Point(gem.X, gem.Y), 3.5, 3.5);

        foreach (var e in g.Enemies)
        {
            double s = e.R * 2.4;
            var r = new Rect(e.X - s / 2, e.Y - s / 2, s, s);
            dc.DrawEllipse(ShadowBrush, null, new Point(e.X, e.Y + s / 2 - 2), s / 2.6, s / 6);
            dc.DrawImage(EnemySprite(e.Kind), r);
            bool blink = e.Fuse > 0 && ((int)(e.Fuse * 14)) % 2 == 0;
            if (e.Flash > 0 || blink) dc.DrawRectangle(FlashBrush, null, r);
            if (e.Kind != EKind.Boss && e.Hp < e.MaxHp)
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(r.X, r.Y - 5, s, 3));
                dc.DrawRectangle(Brushes.LimeGreen, null, new Rect(r.X, r.Y - 5, s * Math.Max(0, e.Hp) / e.MaxHp, 3));
            }
        }

        // 玩家（受傷後的無敵時間會閃爍）
        bool hide = g.Invuln > 0 && ((int)(g.Invuln * 20)) % 2 == 0;
        var pr = new Rect(g.PX - 14, g.PY - 14, 28, 28);
        dc.DrawEllipse(ShadowBrush, null, new Point(g.PX, g.PY + 13), 11, 4);
        if (!hide)
        {
            dc.DrawImage(PlayerSprite ?? PixelArt.Player, pr);
            if (PlayerSprite is not null) dc.DrawRectangle(null, new Pen(Brushes.Black, 1), pr);
        }

        foreach (var b in g.Bullets)
        {
            if (b.Friendly)
            {
                double len = Math.Sqrt(b.VX * b.VX + b.VY * b.VY) + 1e-6;
                double tx = b.VX / len * 9, ty = b.VY / len * 9;
                dc.DrawLine(b.Big ? CritPen : ArrowPen, new Point(b.X - tx, b.Y - ty), new Point(b.X + tx, b.Y + ty));
            }
            else dc.DrawEllipse(b.Big ? BossBullet : EnemyBullet, null, new Point(b.X, b.Y), b.R, b.R);
        }

        foreach (var fx in g.Effects)
        {
            double p = fx.T / fx.Max;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(160 * (1 - p)), 255, 190, 80));
            dc.DrawEllipse(brush, null, new Point(fx.X, fx.Y), fx.Radius * (0.4 + 0.6 * p), fx.Radius * (0.4 + 0.6 * p));
        }

        if (g.HurtFlash > 0) dc.DrawRectangle(HurtBrush, null, new Rect(0, 0, W, H));

        // 王的血條（場地上方）
        var boss = g.Boss;
        if (boss is not null)
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(120, 8, W - 240, 8));
            dc.DrawRectangle(Brushes.Gold, null, new Rect(120, 8, (W - 240) * Math.Max(0, boss.Hp) / boss.MaxHp, 8));
        }
    }
}

/// 「更多 → 地城探險」：即時彈幕肉鴿。WASD／方向鍵移動（自動瞄準射擊）、P 暫停、R 重來；波與波之間的商店用滑鼠點。
public class RoguelikePage : UserControl
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static readonly Dictionary<string, BitmapSource> HeadCache = new();

    readonly RoguelikeGame _g = new();
    readonly RoguelikeView _view;
    readonly Account? _account;
    readonly HashSet<Key> _down = new();
    readonly System.Diagnostics.Stopwatch _sw = new();
    readonly TextBlock _info = new() { FontSize = 14 };
    readonly TextBlock _hpText = new() { FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _hpFill = new() { Background = new SolidColorBrush(Color.FromRgb(200, 50, 60)), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    readonly Border _shop = new() { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(235, 16, 16, 18)), Padding = new Thickness(16), CornerRadius = new CornerRadius(8) };
    readonly Border _end = new() { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)), Padding = new Thickness(16), CornerRadius = new CornerRadius(8) };
    readonly Brush _text, _muted;
    RState _shown = RState.Playing;
    bool _paused, _running;
    double _last;
    const double HpBarWidth = 220;

    /// account：目前選的帳號。正版帳號會把玩家換成自己的皮膚頭像（mc-heads.net，與主畫面頭像同一個來源）；離線帳號或抓不到就用預設小人。
    public RoguelikePage(Account? account = null)
    {
        _account = account;
        _text = (Brush)Application.Current.FindResource("TextBrush");
        _muted = (Brush)Application.Current.FindResource("MutedBrush");
        Foreground = _text;
        _info.Foreground = _text;
        _view = new RoguelikeView(_g);

        var hpBar = new Grid { Width = HpBarWidth, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        hpBar.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(40, 20, 24)), CornerRadius = new CornerRadius(3) });
        hpBar.Children.Add(_hpFill);
        hpBar.Children.Add(_hpText);
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(hpBar);
        top.Children.Add(_info);
        DockPanel.SetDock(top, Dock.Top);

        var hint = new TextBlock
        {
            Text = "WASD／方向鍵移動，自動瞄準射擊　P＝暫停　R＝重來　每波結束可買強化",
            Foreground = _muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(hint, Dock.Bottom);

        var stage = new Grid();
        stage.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = _view });
        _shop.HorizontalAlignment = HorizontalAlignment.Center; _shop.VerticalAlignment = VerticalAlignment.Center; _shop.MaxWidth = 520;
        _end.HorizontalAlignment = HorizontalAlignment.Center; _end.VerticalAlignment = VerticalAlignment.Center; _end.MinWidth = 280;
        stage.Children.Add(_shop);
        stage.Children.Add(_end);

        var root = new DockPanel { Margin = new Thickness(8) };
        root.Children.Add(top);
        root.Children.Add(hint);
        root.Children.Add(stage);
        Content = root;

        Focusable = true;
        Loaded += async (_, _) => { Start(); Focus(); await LoadHeadAsync(); };
        Unloaded += (_, _) => Stop();                       // 離開頁面就停掉遊戲迴圈，避免背景一直跑
        MouseDown += (_, _) => Focus();
        LostKeyboardFocus += (_, _) => _down.Clear();
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, e) => { if (_down.Remove(e.Key)) e.Handled = true; };
    }

    void Start()
    {
        if (_running) return;
        _running = true;
        _sw.Restart(); _last = 0;
        System.Windows.Media.CompositionTarget.Rendering += OnFrame;
    }

    void Stop()
    {
        if (!_running) return;
        _running = false;
        System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
        _down.Clear();
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double now = _sw.Elapsed.TotalSeconds;
        double dt = Math.Min(0.05, now - _last);              // 切分頁或卡頓後不要一次跳太多
        _last = now;

        if (!_paused && _g.State == RState.Playing)
        {
            double ix = (Held(Key.Right, Key.D) ? 1 : 0) - (Held(Key.Left, Key.A) ? 1 : 0);
            double iy = (Held(Key.Down, Key.S) ? 1 : 0) - (Held(Key.Up, Key.W) ? 1 : 0);
            _g.Update(dt, ix, iy);
        }
        if (_g.State != _shown) { _shown = _g.State; ApplyState(); }
        UpdateHud();
        _view.Invalidate();
    }

    bool Held(Key a, Key b) => _down.Contains(a) || _down.Contains(b);

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.P: if (_g.State == RState.Playing) _paused = !_paused; e.Handled = true; return;
            case Key.R: Restart(); e.Handled = true; return;
            case Key.Left: case Key.Right: case Key.Up: case Key.Down:
            case Key.A: case Key.D: case Key.W: case Key.S:
                _down.Add(e.Key); e.Handled = true; return;
        }
    }

    void Restart()
    {
        _g.Reset();
        _paused = false; _down.Clear();
        _shown = RState.Playing; ApplyState();
    }

    // ───────────── 介面：血條、商店、結束畫面 ─────────────
    void UpdateHud()
    {
        _hpFill.Width = HpBarWidth * Math.Clamp(_g.Hp / _g.MaxHp, 0, 1);
        _hpText.Text = $"{Math.Ceiling(_g.Hp)} / {_g.MaxHp}";
        string time = _g.IsBossWave ? "擊敗凋零骷髏王！" : $"剩餘 {Math.Ceiling(_g.TimeLeft)} 秒";
        _info.Text = $"第 {_g.Wave}／{RoguelikeGame.LastWave} 波　{(_paused ? "【已暫停】" : time)}　材料 {_g.Materials}　擊殺 {_g.Kills}";
    }

    void ApplyState()
    {
        _shop.Visibility = _g.State == RState.Shop ? Visibility.Visible : Visibility.Collapsed;
        _end.Visibility = _g.State is RState.Dead or RState.Won ? Visibility.Visible : Visibility.Collapsed;
        if (_g.State == RState.Shop) BuildShop();
        if (_g.State is RState.Dead or RState.Won) BuildEnd();
    }

    void BuildEnd()
    {
        bool won = _g.State == RState.Won;
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = won ? "你征服了地城！" : "你倒下了……", FontSize = 26, FontWeight = FontWeights.Bold, Foreground = won ? Brushes.Gold : Brushes.IndianRed, HorizontalAlignment = HorizontalAlignment.Center });
        p.Children.Add(new TextBlock
        {
            Text = $"撐到第 {_g.Wave} 波　擊殺 {_g.Kills}　（本次最高 第 {RoguelikeGame.Best} 波）",
            Foreground = _text, Margin = new Thickness(0, 10, 0, 14), HorizontalAlignment = HorizontalAlignment.Center
        });
        var again = new Button { Content = "再來一次", Height = 34, MinWidth = 120, Focusable = false };
        again.Click += (_, _) => { Restart(); Focus(); };
        p.Children.Add(again);
        _end.Child = p;
    }

    void BuildShop()
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = $"第 {_g.Wave} 波結束　·　商店", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = _text });
        p.Children.Add(new TextBlock { Text = $"材料 {_g.Materials}　（生命已回復一部分）", Foreground = _muted, Margin = new Thickness(0, 2, 0, 10) });

        for (int i = 0; i < _g.Offers.Count; i++)
        {
            int idx = i; var item = _g.Offers[i];
            bool afford = _g.Materials >= item.Cost;
            var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            label.Children.Add(new TextBlock { Text = item.Sold ? $"{item.Name}（已購買）" : $"{item.Name}　{item.Cost} 材料", FontWeight = FontWeights.Bold, Foreground = _text });
            label.Children.Add(new TextBlock { Text = item.Desc, Foreground = _muted, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
            var b = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6), IsEnabled = !item.Sold && afford, Focusable = false };
            b.Click += (_, _) => { if (_g.Buy(idx)) BuildShop(); };
            p.Children.Add(b);
        }

        p.Children.Add(new TextBlock { Text = StatsText(), Foreground = _muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var reroll = new Button { Content = $"重抽（{_g.RerollCost}）", Height = 34, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), IsEnabled = _g.Materials >= _g.RerollCost, Focusable = false };
        reroll.Click += (_, _) => { if (_g.Reroll()) BuildShop(); };
        var next = new Button { Content = _g.Wave + 1 == RoguelikeGame.LastWave ? "迎戰凋零骷髏王" : $"開始第 {_g.Wave + 1} 波", Height = 34, Padding = new Thickness(18, 0, 18, 0), Focusable = false };
        next.Click += (_, _) => { _g.NextWave(); _down.Clear(); _shown = _g.State; ApplyState(); Focus(); };
        row.Children.Add(reroll); row.Children.Add(next);
        p.Children.Add(row);
        _shop.Child = p;
    }

    string StatsText() =>
        $"傷害 {_g.Dmg:0.#}　射速 {_g.FireRate:0.##}/秒　射程 {_g.Range:0}　移速 {_g.Speed:0}　護甲 {_g.Armor:0}　" +
        $"最大生命 {_g.MaxHp:0}　回復 {_g.Regen:0.#}/秒　拾取 {_g.Pickup:0}　箭數 {_g.Multi}　穿透 {_g.Pierce}";

    async Task LoadHeadAsync()
    {
        if (_account is not { Type: AccountType.Microsoft, Uuid.Length: > 0 }) return;   // 離線帳號：維持預設
        var uuid = _account.Uuid;
        if (!HeadCache.TryGetValue(uuid, out var bmp))
        {
            try
            {
                // 32px＝8×8 頭像放大 4 倍，最近鄰縮放不會糊
                var bytes = await Http.GetByteArrayAsync($"https://mc-heads.net/avatar/{uuid}/32");
                var img = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
                bmp = img; HeadCache[uuid] = img;
            }
            catch { return; }                                    // 沒網路或服務掛了：維持預設
        }
        _view.PlayerSprite = bmp;
    }
}
