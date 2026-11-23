using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace IEML.App;

/// 主題的所有顏色（#RRGGBB）。預設值＝Theme.xaml 原本的配色。
public sealed class ThemeSettings
{
    public string Accent { get; set; } = "#00FFFF";
    public string Window { get; set; } = "#0F0F0F";
    public string Panel { get; set; } = "#1C1C1C";
    public string Line { get; set; } = "#333333";
    public string Text { get; set; } = "#FFFFFF";
    public string Muted { get; set; } = "#888888";
    public string OnAccent { get; set; } = "#0F0F0F";

    /// 可調整的部分：欄位名、標題、說明；以及它對應的 Theme.xaml 資源名。
    public static readonly (string Key, string Title, string Hint, string Resource)[] Parts =
    {
        ("Accent",   "強調色",          "主按鈕、進度條、高亮、發光",     "AccentBrush"),
        ("Window",   "視窗背景",        "最底層的背景",                   "WindowBg"),
        ("Panel",    "面板／卡片底色",  "左右欄與卡片的底色",             "PanelBg"),
        ("Line",     "外框與分隔線",    "",                               "LineBrush"),
        ("Text",     "主要文字",        "",                               "TextBrush"),
        ("Muted",    "圖標／次要文字",  "說明文字、圖標",                 "MutedBrush"),
        ("OnAccent", "強調色上的文字",  "主按鈕（青底）上的字色",         "OnAccentBrush"),
    };

    public string Get(string key) => key switch
    {
        "Accent" => Accent, "Window" => Window, "Panel" => Panel, "Line" => Line,
        "Text" => Text, "Muted" => Muted, "OnAccent" => OnAccent, _ => "#FFFFFF"
    };

    public void Set(string key, string value)
    {
        switch (key)
        {
            case "Accent": Accent = value; break;
            case "Window": Window = value; break;
            case "Panel": Panel = value; break;
            case "Line": Line = value; break;
            case "Text": Text = value; break;
            case "Muted": Muted = value; break;
            case "OnAccent": OnAccent = value; break;
        }
    }

    public void CopyFrom(ThemeSettings o) { foreach (var p in Parts) Set(p.Key, o.Get(p.Key)); }

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { color = (Color)ColorConverter.ConvertFromString(text.Trim()); return true; }
        catch { return false; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// 配色編輯器：每個部分一列（色塊＋名稱＋色碼），點開有 R、G、B 三條拉條；拖動時即時套用。
public sealed class ThemeEditor : StackPanel
{
    sealed class Row
    {
        public readonly Slider[] Sliders = new Slider[3];
        public readonly TextBlock[] Values = new TextBlock[3];
        public Border Swatch = null!;
        public TextBlock Hex = null!;
    }

    readonly ThemeSettings _theme;
    readonly Action _changed;
    readonly Dictionary<string, Row> _rows = new();
    bool _building;

    public ThemeEditor(ThemeSettings theme, Action changed)
    {
        _theme = theme; _changed = changed;
        foreach (var p in ThemeSettings.Parts) Children.Add(BuildPart(p.Key, p.Title, p.Hint));
        Refresh();
    }

    static void Bind(FrameworkElement el, DependencyProperty prop, string resource) => el.SetResourceReference(prop, resource);

    UIElement BuildPart(string key, string title, string hint)
    {
        var row = new Row(); _rows[key] = row;
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

        var arrow = new TextBlock { Text = "▸", Width = 16, VerticalAlignment = VerticalAlignment.Center };
        Bind(arrow, TextBlock.ForegroundProperty, "MutedBrush");
        row.Swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
        Bind(row.Swatch, Border.BorderBrushProperty, "LineBrush");
        var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Bind(name, TextBlock.ForegroundProperty, "TextBrush");
        row.Hex = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        Bind(row.Hex, TextBlock.ForegroundProperty, "MutedBrush");

        var headRow = new StackPanel { Orientation = Orientation.Horizontal };
        headRow.Children.Add(arrow); headRow.Children.Add(row.Swatch); headRow.Children.Add(name); headRow.Children.Add(row.Hex);
        var head = new Border { Child = headRow, Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(0, 4, 0, 4) };

        var body = new StackPanel { Margin = new Thickness(26, 2, 0, 6), Visibility = Visibility.Collapsed };
        if (hint.Length > 0)
        {
            var h = new TextBlock { Text = hint, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
            Bind(h, TextBlock.ForegroundProperty, "MutedBrush");
            body.Children.Add(h);
        }
        for (int i = 0; i < 3; i++)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            var lbl = new TextBlock { Text = "RGB"[i].ToString(), VerticalAlignment = VerticalAlignment.Center };
            Bind(lbl, TextBlock.ForegroundProperty, "MutedBrush");
            var sl = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 16, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            var val = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Bind(val, TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(sl, 1); Grid.SetColumn(val, 2);
            g.Children.Add(lbl); g.Children.Add(sl); g.Children.Add(val);
            sl.ValueChanged += (_, _) => OnSlide(key);
            row.Sliders[i] = sl; row.Values[i] = val;
            body.Children.Add(g);
        }

        head.MouseLeftButtonUp += (_, _) =>
        {
            var open = body.Visibility != Visibility.Visible;
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            arrow.Text = open ? "▾" : "▸";
        };
        box.Children.Add(head); box.Children.Add(body);
        return box;
    }

    void OnSlide(string key)
    {
        if (_building) return;
        var r = _rows[key];
        var c = Color.FromRgb((byte)Math.Round(r.Sliders[0].Value), (byte)Math.Round(r.Sliders[1].Value), (byte)Math.Round(r.Sliders[2].Value));
        _theme.Set(key, ThemeSettings.ToHex(c));
        UpdateHead(key, c);
        _changed();
    }

    void UpdateHead(string key, Color c)
    {
        var r = _rows[key];
        r.Swatch.Background = new SolidColorBrush(c);
        r.Hex.Text = ThemeSettings.ToHex(c);
        r.Values[0].Text = c.R.ToString(); r.Values[1].Text = c.G.ToString(); r.Values[2].Text = c.B.ToString();
    }

    /// 依目前的 ThemeSettings 重設所有拉條（按預設色塊或「恢復預設」之後呼叫；不會觸發 changed）。
    public void Refresh()
    {
        _building = true;
        try
        {
            foreach (var p in ThemeSettings.Parts)
            {
                ThemeSettings.TryParse(_theme.Get(p.Key), out var c);
                var r = _rows[p.Key];
                r.Sliders[0].Value = c.R; r.Sliders[1].Value = c.G; r.Sliders[2].Value = c.B;
                UpdateHead(p.Key, c);
            }
        }
        finally { _building = false; }
    }
}
