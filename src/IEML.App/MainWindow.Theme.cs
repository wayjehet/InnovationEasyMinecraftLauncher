using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using IEML.Core.Services;
namespace IEML.App;

/// 主題（配色）：載入／套用／儲存。編輯介面在 ThemeEditor（設置頁）。
public partial class MainWindow
{
    static readonly string ThemePath = Path.Combine(AppPaths.Root, "theme.json");
    readonly ThemeSettings _theme = new();
    DispatcherTimer? _themeSaveTimer;

    /// 就地改色（資源裡的刷子若沒被凍結）：用 StaticResource 的地方（視窗背景、面板）也會立刻跟著變；
    /// 被凍結就換成新刷子（只有之後建立的畫面才會用到）。
    static void SetBrush(string key, Color c)
    {
        if (Application.Current.TryFindResource(key) is SolidColorBrush b && !b.IsFrozen) b.Color = c;
        else Application.Current.Resources[key] = new SolidColorBrush(c);
    }

    static void SetEffectColor(string key, Color c)
    {
        if (Application.Current.TryFindResource(key) is not DropShadowEffect e) return;
        if (!e.IsFrozen) { e.Color = c; return; }
        var n = (DropShadowEffect)e.Clone(); n.Color = c; Application.Current.Resources[key] = n;
    }

    void ApplyTheme(bool save = true)
    {
        foreach (var p in ThemeSettings.Parts)
            if (ThemeSettings.TryParse(_theme.Get(p.Key), out var c)) SetBrush(p.Resource, c);
        if (ThemeSettings.TryParse(_theme.Accent, out var accent))
        {
            SetEffectColor("GlowEffect", accent);
            SetEffectColor("SoftGlowEffect", accent);
        }
        if (save) QueueSaveTheme();
    }

    // 拖拉拉條時不要每一格都寫檔：停下來 0.5 秒後才存
    void QueueSaveTheme()
    {
        _themeSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _themeSaveTimer.Tick -= SaveThemeTick;
        _themeSaveTimer.Tick += SaveThemeTick;
        _themeSaveTimer.Stop(); _themeSaveTimer.Start();
    }

    void SaveThemeTick(object? sender, EventArgs e)
    {
        _themeSaveTimer?.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
            File.WriteAllText(ThemePath, JsonSerializer.Serialize(_theme));
        }
        catch { }
    }

    void LoadTheme()
    {
        try
        {
            if (!File.Exists(ThemePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(ThemePath));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var key = prop.Name == "Icon" ? "Muted" : prop.Name; // 舊版 theme.json 的欄位名
                if (prop.Value.ValueKind == JsonValueKind.String && ThemeSettings.Parts.Any(x => x.Key == key)
                    && ThemeSettings.TryParse(prop.Value.GetString(), out _))
                    _theme.Set(key, prop.Value.GetString()!.Trim());
            }
            ApplyTheme(save: false);
        }
        catch { }
    }
}
