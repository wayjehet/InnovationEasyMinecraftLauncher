using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Plugins;
namespace IEML.App;

static class PluginUi
{
    public static TextBlock Text(string s, double size = 13, bool bold = false, bool muted = false, bool wrap = true)
    {
        var t = new TextBlock { Text = s, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, Margin = new Thickness(0, 2, 0, 2) };
        if (bold) t.FontWeight = FontWeights.Bold;
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
        return t;
    }

    public static void MergeStyles(FrameworkElement fe) =>
        fe.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PageStyles.xaml", UriKind.Relative) });
}

/// 把插件回傳的「元件樹（JSON）」畫成 WPF 控制項。插件拿不到任何 WPF 物件，只能描述，由這裡決定怎麼畫。
/// 元件：heading、text、button、input、checkbox、list、row、column、separator；深度 ≤ 8、總數 ≤ 500、文字 ≤ 2000 字。
public sealed class PluginPageView : UserControl
{
    readonly PluginRuntime _rt;
    readonly string _pageId;
    readonly Dictionary<string, Func<JsonNode?>> _getters = new();
    int _count;

    public PluginPageView(PluginRuntime rt, string pageId)
    {
        _rt = rt; _pageId = pageId;
        PluginUi.MergeStyles(this);
        SetResourceReference(ForegroundProperty, "TextBrush");
        Loaded += (_, _) => Render(new JsonObject());
    }

    void Render(JsonObject values)
    {
        _getters.Clear(); _count = 0;
        var root = new StackPanel { Margin = new Thickness(8) };
        root.Children.Add(PluginUi.Text($"此頁面由插件「{_rt.Plugin.Manifest.Name}」提供（在沙盒中執行）", 11, muted: true));

        var json = _rt.Dispatch("page.render", new JsonObject { ["id"] = _pageId, ["values"] = values.DeepClone() });
        if (json is null)
            root.Children.Add(PluginUi.Text(_rt.LastError ?? "這個頁面沒有內容。", 13));
        else
        {
            try { root.Children.Add(Build(JsonNode.Parse(json), 0)); }
            catch (Exception ex) { root.Children.Add(PluginUi.Text("頁面格式錯誤：" + ex.Message, 13)); }
        }
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    JsonObject Collect()
    {
        var o = new JsonObject();
        foreach (var (k, get) in _getters) o[k] = get();
        return o;
    }

    void OnAction(string action)
    {
        var values = Collect();
        _rt.Dispatch("page.action", new JsonObject { ["id"] = _pageId, ["action"] = action, ["values"] = values.DeepClone() });
        Render(values);      // 動作後重畫（輸入框的值會以 values 傳回給 render）
    }

    UIElement Build(JsonNode? n, int depth)
    {
        if (depth > 8 || ++_count > 500) return PluginUi.Text("…", 12, muted: true);
        var type = J.S(J.Get(n, "type")) ?? "";
        switch (type)
        {
            case "heading": return PluginUi.Text(J.Clip(J.S(J.Get(n, "text")), 2000), 18, bold: true);
            case "text": return PluginUi.Text(J.Clip(J.S(J.Get(n, "text")), 2000), 13, muted: J.B(J.Get(n, "muted")));

            case "button":
            {
                var action = J.Clip(J.S(J.Get(n, "action")), 60);
                var b = new Button { Content = J.Clip(J.S(J.Get(n, "label")), 60), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
                if (!J.B(J.Get(n, "primary"))) b.SetResourceReference(StyleProperty, "GhostButton");
                b.Click += (_, _) => OnAction(action);
                return b;
            }

            case "input":
            {
                var id = J.Clip(J.S(J.Get(n, "id")), 40);
                var p = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                var label = J.S(J.Get(n, "label"));
                if (!string.IsNullOrEmpty(label)) p.Children.Add(PluginUi.Text(J.Clip(label, 100), 12, muted: true));
                var tb = new TextBox { Text = J.Clip(J.S(J.Get(n, "value")), 2000), MaxLength = 2000, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
                p.Children.Add(tb);
                if (id.Length > 0) _getters[id] = () => JsonValue.Create(tb.Text);
                return p;
            }

            case "checkbox":
            {
                var id = J.Clip(J.S(J.Get(n, "id")), 40);
                var cb = new CheckBox { Content = J.Clip(J.S(J.Get(n, "label")), 200), IsChecked = J.B(J.Get(n, "value")), Margin = new Thickness(0, 4, 0, 4) };
                if (id.Length > 0) _getters[id] = () => JsonValue.Create(cb.IsChecked == true);
                return cb;
            }

            case "list":
            {
                var p = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                foreach (var it in (J.Get(n, "items") as JsonArray)?.Take(100) ?? Enumerable.Empty<JsonNode?>())
                    p.Children.Add(PluginUi.Text("• " + J.Clip(J.S(it), 300), 13));
                return p;
            }

            case "row":
            {
                var w = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var c in (J.Get(n, "children") as JsonArray)?.Take(50) ?? Enumerable.Empty<JsonNode?>()) w.Children.Add(Build(c, depth + 1));
                return w;
            }

            case "column":
            case "stack":
            {
                var p = new StackPanel();
                foreach (var c in (J.Get(n, "children") as JsonArray)?.Take(100) ?? Enumerable.Empty<JsonNode?>()) p.Children.Add(Build(c, depth + 1));
                return p;
            }

            case "separator":
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
                line.SetResourceReference(Border.BackgroundProperty, "LineBrush");
                return line;
            }

            default: return PluginUi.Text($"（不支援的元件：{J.Clip(type, 20)}）", 12, muted: true);
        }
    }
}

/// 「更多 → 插件」：插件清單、啟用開關、逐項權限、插件提供的頁面、紀錄。
public sealed class PluginsPage : UserControl
{
    readonly PluginManager _mgr;
    readonly StackPanel _list = new();

    public PluginsPage(PluginManager mgr)
    {
        _mgr = mgr;
        PluginUi.MergeStyles(this);
        SetResourceReference(ForegroundProperty, "TextBrush");

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var bar = new WrapPanel();
        var open = new Button { Content = "開啟插件資料夾", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6) };
        open.Click += (_, _) => _mgr.OpenFolder();
        var reload = new Button { Content = "重新載入", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6) };
        reload.SetResourceReference(StyleProperty, "GhostButton");
        reload.Click += (_, _) => _mgr.LoadAll();
        bar.Children.Add(open); bar.Children.Add(reload);
        top.Children.Add(bar);
        top.Children.Add(PluginUi.Text("插件在沙盒中執行：預設全部停用、沒有任何權限。每個權限都要你逐項勾選，插件的程式碼怎麼寫都繞不過這個檢查。", 12, muted: true));

        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Content = dock;

        Loaded += (_, _) => { _mgr.Changed += Rebuild; Rebuild(); };
        Unloaded += (_, _) => _mgr.Changed -= Rebuild;
    }

    void Rebuild()
    {
        _list.Children.Clear();
        if (_mgr.Installed.Count == 0)
            _list.Children.Add(PluginUi.Text("還沒有插件。把插件資料夾（含 plugin.json 與 main.js）放進插件資料夾後按「重新載入」。", 13, muted: true));

        foreach (var p in _mgr.Installed)
        {
            var m = p.Manifest;
            var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) };
            card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            var body = new StackPanel();
            body.Children.Add(PluginUi.Text($"{m.Name}　v{m.Version}" + (m.Author.Length > 0 ? $"　作者：{m.Author}" : ""), 15, bold: true));
            if (m.Description.Length > 0) body.Children.Add(PluginUi.Text(m.Description, 12, muted: true));

            if (p.Error is not null)
            {
                var err = PluginUi.Text("無法載入：" + p.Error, 12);
                err.Foreground = System.Windows.Media.Brushes.IndianRed;
                body.Children.Add(err);
                card.Child = body; _list.Children.Add(card);
                continue;
            }

            var st = _mgr.StateOf(m.Id);
            var enable = new CheckBox { Content = "啟用這個插件", IsChecked = st.Enabled, Margin = new Thickness(0, 8, 0, 4) };
            var id = m.Id;
            enable.Click += (_, _) => _mgr.SetEnabled(id, enable.IsChecked == true);
            body.Children.Add(enable);

            if (_mgr.Running.TryGetValue(m.Id, out var rt) && rt.LastError is not null)
            {
                var err = PluginUi.Text(rt.LastError, 12);
                err.Foreground = System.Windows.Media.Brushes.IndianRed;
                body.Children.Add(err);
            }

            if (m.Permissions.Count == 0) body.Children.Add(PluginUi.Text("這個插件沒有要求任何權限。", 12, muted: true));
            else
            {
                body.Children.Add(PluginUi.Text("權限（預設全部關閉，勾選才會授權）：", 12, muted: true));
                foreach (var perm in m.Permissions)
                {
                    var (text, danger) = Perm.All[perm];
                    var label = (danger ? "⚠ " : "") + text + (perm == Perm.NetHttp ? $"　網域：{string.Join("、", m.NetworkHosts)}" : "");
                    var cb = new CheckBox { Content = label, IsChecked = st.Granted.Contains(perm), Margin = new Thickness(8, 3, 0, 3) };
                    if (danger) cb.Foreground = System.Windows.Media.Brushes.Orange;
                    var pc = perm;
                    cb.Click += (_, _) => _mgr.SetGranted(id, pc, cb.IsChecked == true);
                    body.Children.Add(cb);
                }
            }

            if (st.Enabled && rt is { Loaded: true } && PluginGate.Has(m, st, Perm.UiPage) && rt.Pages.Count > 0)
            {
                var pages = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var (pid, info) in rt.Pages)
                {
                    var pageId = pid;
                    var b = new Button { Content = "開啟：" + info.Title, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6) };
                    b.Click += (_, _) => _mgr.OpenPageDeferred(rt, pageId);
                    pages.Children.Add(b);
                }
                body.Children.Add(pages);
            }

            card.Child = body;
            _list.Children.Add(card);
        }

        string[] logs; lock (_mgr.Logs) logs = _mgr.Logs.TakeLast(12).ToArray();
        if (logs.Length > 0)
        {
            _list.Children.Add(PluginUi.Text("最近紀錄（含被拒絕的呼叫）：", 12, muted: true));
            _list.Children.Add(PluginUi.Text(string.Join("\n", logs), 11, muted: true));
        }
    }
}
