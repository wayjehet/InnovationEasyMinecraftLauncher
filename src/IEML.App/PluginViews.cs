using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    readonly Dictionary<string, TextBox> _inputs = new();
    readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };   // 合併短時間內的多次通知（串流每個事件一次）
    JsonObject? _keep;      // 自動重畫時：保留使用者目前輸入的值，不被插件的預設值蓋掉
    int _count;

    public PluginPageView(PluginRuntime rt, string pageId)
    {
        _rt = rt; _pageId = pageId;
        PluginUi.MergeStyles(this);
        SetResourceReference(ForegroundProperty, "TextBrush");
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); if (IsLoaded && _rt.Loaded) Refresh(); };
        Loaded += (_, _) =>
        {
            _rt.PageInvalidated -= OnInvalidated; _rt.PageInvalidated += OnInvalidated;
            Render(new JsonObject());
        };
        Unloaded += (_, _) => { _rt.PageInvalidated -= OnInvalidated; _refreshTimer.Stop(); };
    }

    void OnInvalidated() => Dispatcher.InvokeAsync(() => { if (!_refreshTimer.IsEnabled) _refreshTimer.Start(); });

    /// 非同步 callback 之後的自動重畫：保留捲動位置、輸入框內容與游標。
    void Refresh()
    {
        var values = Collect();
        var offset = (Content as ScrollViewer)?.VerticalOffset ?? 0;
        string? focusId = null; int caret = 0;
        foreach (var (k, tb) in _inputs) if (tb.IsKeyboardFocused) { focusId = k; caret = tb.CaretIndex; }
        _keep = values;
        try { Render(values); } finally { _keep = null; }
        if (Content is ScrollViewer sv) { sv.UpdateLayout(); sv.ScrollToVerticalOffset(offset); }
        if (focusId is not null && _inputs.TryGetValue(focusId, out var nt)) { nt.Focus(); nt.CaretIndex = Math.Min(caret, nt.Text.Length); }
    }

    void Render(JsonObject values)
    {
        _getters.Clear(); _inputs.Clear(); _count = 0;
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
                var init = J.Clip(J.S(J.Get(n, "value")), 2000);
                if (_keep is not null && id.Length > 0 && _keep.TryGetPropertyValue(id, out var kept) && J.S(kept) is { } keptText) init = keptText;
                var tb = new TextBox { Text = init, MaxLength = 2000, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
                p.Children.Add(tb);
                if (id.Length > 0) { _getters[id] = () => JsonValue.Create(tb.Text); _inputs[id] = tb; }
                return p;
            }

            case "checkbox":
            {
                var id = J.Clip(J.S(J.Get(n, "id")), 40);
                var chk = J.B(J.Get(n, "value"));
                if (_keep is not null && id.Length > 0 && _keep.TryGetPropertyValue(id, out var keptChk) && keptChk is JsonValue kv && kv.TryGetValue<bool>(out var kb)) chk = kb;
                var cb = new CheckBox { Content = J.Clip(J.S(J.Get(n, "label")), 200), IsChecked = chk, Margin = new Thickness(0, 4, 0, 4) };
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
                    if (perm == Perm.Exec && m.Exec.Count > 0)
                        label += "　程式：" + string.Join("、", m.Exec.Select(d => d.Id));
                    var cb = new CheckBox { Content = label, IsChecked = st.Granted.Contains(perm), Margin = new Thickness(8, 3, 0, 3) };
                    if (danger) cb.Foreground = System.Windows.Media.Brushes.Orange;
                    var pc = perm;
                    cb.Click += async (_, _) =>
                    {
                        if (pc == Perm.Exec && cb.IsChecked == true)
                        {
                            // exec 要先同意協議；同意綁定插件版本與程式清單，任何一項變動都要重新同意
                            var list = string.Join("\n", m.Exec.Select(d => $"• {d.Id}（{d.File}）" + (d.Description.Length > 0 ? "：" + d.Description : "")));
                            var ok = await Dialogs.ConfirmAsync("允許插件執行外部程式",
                                $"插件「{m.Name}」要求能執行下列程式：\n{list}\n\n" +
                                "• 這些程式會以你的 Windows 權限執行，沙盒管不到它們，可能讀寫你的檔案或連網。\n" +
                                "• 每次執行前，啟動器都會再問你一次。\n• 程式檔案被更換（雜湊不符）時會拒絕執行。\n• 插件更新或程式清單變動後，需要重新同意。",
                                "我了解風險，允許", "取消");
                            if (!ok) { cb.IsChecked = false; return; }
                            _mgr.AcceptExec(id);
                            return;
                        }
                        _mgr.SetGranted(id, pc, cb.IsChecked == true);
                    };
                    body.Children.Add(cb);
                }
            }

            // 金鑰：由使用者填入，主程式保管；插件只能指名使用，讀不到內容
            foreach (var sd in m.Secrets)
            {
                var hasKey = _mgr.HasSecret(id, sd.Name);
                body.Children.Add(PluginUi.Text($"金鑰：{sd.Label}　只會送往 {sd.Host}，插件讀不到內容　狀態：{(hasKey ? "已設定" : "未設定")}", 12));
                var krow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 2, 0, 6) };
                var pb = new PasswordBox { Width = 260, MaxLength = 4000, Margin = new Thickness(0, 0, 8, 0) };
                var save = new Button { Content = "儲存", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
                var clear = new Button { Content = "清除", Padding = new Thickness(12, 4, 12, 4) };
                clear.SetResourceReference(StyleProperty, "GhostButton");
                var secretName = sd.Name;
                save.Click += (_, _) =>
                {
                    try { _mgr.SetSecret(id, secretName, pb.Password); pb.Clear(); _mgr.RaiseChanged(); }
                    catch (Exception ex) { _ = Dialogs.InfoAsync("無法儲存金鑰", ex.Message); }
                };
                clear.Click += (_, _) =>
                {
                    try { _mgr.SetSecret(id, secretName, ""); _mgr.RaiseChanged(); }
                    catch (Exception ex) { _ = Dialogs.InfoAsync("無法清除金鑰", ex.Message); }
                };
                krow.Children.Add(pb); krow.Children.Add(save); krow.Children.Add(clear);
                body.Children.Add(krow);
            }

            // 釘選到「更多」：插件只能申請，要你核准才會出現
            if (m.Contributes.Pins.Count > 0)
            {
                body.Children.Add(PluginUi.Text("這個插件想在「更多」頁新增入口（核准後才會出現）：", 12, muted: true));
                foreach (var pin in m.Contributes.Pins)
                {
                    var approved = PinPolicy.IsApproved(st.PinApprovals, pin);
                    var changed = st.PinApprovals.ContainsKey(pin.Id) && !approved;
                    var pinLabel = (pin.Icon.Length > 0 ? pin.Icon + " " : "") + $"{pin.Title}（{m.Name}）　→ 開啟頁面「{pin.Page}」" + (changed ? "　⚠ 內容已變更，需要重新核准" : "");
                    var pcb = new CheckBox { Content = pinLabel, IsChecked = approved, Margin = new Thickness(8, 3, 0, 3) };
                    var pinId = pin.Id;
                    pcb.Click += (_, _) => _mgr.SetPinApproved(id, pinId, pcb.IsChecked == true);
                    body.Children.Add(pcb);
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
