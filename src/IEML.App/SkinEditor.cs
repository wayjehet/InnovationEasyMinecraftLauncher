using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IEML.Core.Services;
using Microsoft.Win32;
namespace IEML.App;

/// 設置頁的「自訂圖片」編輯區：每個部位一列（縮圖、狀態、選圖、清除），支援九宮格／像素風設定，
/// 以及素材包（zip）匯出／匯入。變更立刻套用到所有畫面。
public sealed class SkinEditor : StackPanel
{
    public SkinEditor()
    {
        Loaded += (_, _) =>
        {
            SkinManager.Instance.Changed -= Rebuild;
            SkinManager.Instance.Changed += Rebuild;
            Rebuild();
        };
        Unloaded += (_, _) => SkinManager.Instance.Changed -= Rebuild;
    }

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    Button MakeButton(string text, Action click)
    {
        var b = new Button
        {
            Content = text, Height = 32, Margin = new Thickness(0, 0, 8, 6),
            Style = (Style)Application.Current.FindResource("GhostButton")
        };
        b.Click += (_, _) => click();
        return b;
    }

    TextBlock Hint(string text, double size = 11, Thickness? margin = null) => new()
    {
        Text = text, Foreground = Res("MutedBrush"), FontSize = size, TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0, 0, 0, 6)
    };

    void Rebuild()
    {
        Children.Clear();
        var sm = SkinManager.Instance;

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        bar.Children.Add(MakeButton("開啟素材資料夾", OpenFolder));
        bar.Children.Add(MakeButton("匯出素材包", ExportPack));
        bar.Children.Add(MakeButton("匯入素材包", ImportPack));
        bar.Children.Add(MakeButton("全部清除", ClearAll));
        Children.Add(bar);
        Children.Add(Hint("每個部位都可以換圖：按「選圖」，或把 PNG 以「部位名.png」放進素材資料夾，變更會立刻套用、不用重開。" +
                          "沒放圖的部位使用預設樣式。按鈕有「滑過／按下／停用」三種狀態圖，沒放就沿用一般圖。" +
                          "「九宮格邊距」＞0 時，圖的四個角不會被拉伸，適合會變大小的按鈕與面板。"));

        string? group = null;
        foreach (var slot in SkinManager.Slots)
        {
            if (slot.Group != group)
            {
                group = slot.Group;
                Children.Add(new TextBlock { Text = group, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 14, 0, 4) });
            }
            Children.Add(Row(slot.Key, slot.Title, slot.Hint, false));
            if (sm.Get(slot.Key) is not null && slot.Key != "logo") Children.Add(OptionsRow(slot));
            if (slot.States)
                foreach (var (suffix, title) in SkinManager.StateInfo)
                    Children.Add(Row(slot.Key + suffix, "└ " + title, "", true));
        }
    }

    UIElement Row(string key, string title, string hint, bool indent)
    {
        var sm = SkinManager.Instance;
        var g = new Grid { Margin = new Thickness(indent ? 24 : 0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = 48, Height = 30, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            Background = Res("WindowBg"), BorderBrush = Res("LineBrush"), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Image { Source = sm.Get(key), Stretch = Stretch.Uniform, Margin = new Thickness(2) }
        };
        g.Children.Add(thumb);

        bool user = sm.HasUserFile(key), any = sm.Get(key) is not null;
        string status = user ? "● 自訂圖片" : any ? "● 使用 exe 旁的素材" : "○ 預設樣式";
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = status + (string.IsNullOrEmpty(hint) ? "" : "　" + hint) + "　" + key + ".png",
            Foreground = Res("MutedBrush"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        btns.Children.Add(MakeSmall("選圖", () => Pick(key)));
        if (user) btns.Children.Add(MakeSmall("清除", () => Clear(key)));
        Grid.SetColumn(btns, 2);
        g.Children.Add(btns);
        return g;
    }

    Button MakeSmall(string text, Action click)
    {
        var b = MakeButton(text, click);
        b.Height = 28; b.Margin = new Thickness(6, 0, 0, 0);
        return b;
    }

    UIElement OptionsRow(SkinSlot slot)
    {
        var sm = SkinManager.Instance;
        var o = sm.Options(slot.Key);
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(54, 0, 0, 4) };
        p.Children.Add(new TextBlock { Text = "九宮格邊距", Foreground = Res("MutedBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var tb = new TextBox { Width = 46, Text = o.Slice.ToString(), IsEnabled = slot.Key != "background" };
        var cb = new CheckBox { Content = "像素風（不平滑）", IsChecked = o.Pixel, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        p.Children.Add(tb);
        p.Children.Add(new TextBlock { Text = "px", Foreground = Res("MutedBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        p.Children.Add(cb);

        void Apply()
        {
            int n = int.TryParse(tb.Text, out var v) ? Math.Clamp(v, 0, 2048) : 0;
            if (n == o.Slice && (cb.IsChecked == true) == o.Pixel) return;
            try { sm.SetOptions(slot.Key, n, cb.IsChecked == true); }
            catch (Exception ex) { _ = Dialogs.InfoAsync("無法儲存設定", ex.Message); }
        }
        tb.LostFocus += (_, _) => Apply();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };
        cb.Click += (_, _) => Apply();
        return p;
    }

    // ---------- 動作 ----------
    void Pick(string key)
    {
        var dlg = new OpenFileDialog { Title = "選擇圖片", Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有檔案|*.*" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try { SkinManager.Instance.Import(key, dlg.FileName); }
        catch (Exception ex) { _ = Dialogs.InfoAsync("無法套用圖片", ex.Message); }
    }

    void Clear(string key)
    {
        try { SkinManager.Instance.Clear(key); }
        catch (Exception ex) { _ = Dialogs.InfoAsync("無法清除", ex.Message); }
    }

    void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(ArtAssets.UserDir);
            Process.Start(new ProcessStartInfo(ArtAssets.UserDir) { UseShellExecute = true });
        }
        catch (Exception ex) { _ = Dialogs.InfoAsync("無法開啟資料夾", ex.Message); }
    }

    async void ExportPack()
    {
        var dlg = new SaveFileDialog { Title = "匯出素材包", FileName = "IEML-skin.zip", Filter = "素材包 (*.zip)|*.zip" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            int n = SkinManager.Instance.ExportPack(dlg.FileName);
            if (n == 0) { try { File.Delete(dlg.FileName); } catch { } await Dialogs.InfoAsync("匯出素材包", "還沒有自訂圖片可以匯出。"); }
            else await Dialogs.InfoAsync("匯出素材包", $"已匯出 {n} 張圖片（含拉伸設定）。");
        }
        catch (Exception ex) { await Dialogs.InfoAsync("匯出失敗", ex.Message); }
    }

    async void ImportPack()
    {
        var dlg = new OpenFileDialog { Title = "匯入素材包", Filter = "素材包 (*.zip)|*.zip" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!await Dialogs.ConfirmAsync("匯入素材包", "匯入會覆蓋同名的自訂圖片與拉伸設定，確定要匯入嗎？", "匯入", "取消")) return;
        try
        {
            int n = SkinManager.Instance.ImportPack(dlg.FileName);
            await Dialogs.InfoAsync("匯入素材包", n == 0 ? "素材包裡沒有可用的圖片（只接受根目錄、檔名為部位名的 PNG）。" : $"已匯入 {n} 個檔案。");
        }
        catch (Exception ex) { await Dialogs.InfoAsync("匯入失敗", ex.Message); }
    }

    async void ClearAll()
    {
        if (!await Dialogs.ConfirmAsync("全部清除", "要刪除所有自訂圖片與拉伸設定，全部恢復預設樣式嗎？（不會動 exe 旁的素材）", "全部清除", "取消")) return;
        try { SkinManager.Instance.ClearAll(); }
        catch (Exception ex) { await Dialogs.InfoAsync("無法清除", ex.Message); }
    }
}
