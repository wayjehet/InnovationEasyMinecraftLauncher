using System.Windows;
using System.Windows.Controls;
namespace IEML.App;

/// 視窗內的確認／提示（取代 MessageBox）。顯示在主視窗的對話框層（MainWindow.ShowModal）；
/// 主視窗還沒就緒（例如啟動極早期）時退回系統 MessageBox。
/// 注意：同一時間只有一個對話框，在對話框開著時再開，舊的會以「取消」結束。
public static class Dialogs
{
    static MainWindow? Main => Application.Current?.MainWindow as MainWindow is { IsLoaded: true } w ? w : null;

    /// 多按鈕選擇，回傳被按的按鈕索引；按 Esc 回傳 -1。
    public static async Task<int> ChoiceAsync(string title, string message, params string[] buttons)
    {
        if (Main is not { } mw)
        {
            var r = buttons.Length switch
            {
                1 => MessageBox.Show(message, title, MessageBoxButton.OK),
                2 => MessageBox.Show(message, title, MessageBoxButton.YesNo),
                _ => MessageBox.Show(message, title, MessageBoxButton.YesNoCancel)
            };
            return r switch { MessageBoxResult.Yes or MessageBoxResult.OK => 0, MessageBoxResult.No => 1, _ => buttons.Length > 2 ? 2 : -1 };
        }
        int picked = -1;
        UIElement Build(Action<bool> close)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush")
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = new Button { Content = buttons[i], MinWidth = 80, Height = 32, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(8, 0, 0, 0) };
                if (i != 0) b.Style = (Style)Application.Current.FindResource("GhostButton");   // 第一個是主要動作，其餘用淡色
                b.Click += (_, _) => { picked = idx; close(true); };
                row.Children.Add(b);
            }
            // 主要動作放最右邊比較順手：把順序反過來
            var reversed = row.Children.Cast<UIElement>().Reverse().ToList();
            row.Children.Clear();
            foreach (var c in reversed) row.Children.Add(c);
            panel.Children.Add(row);
            return panel;
        }
        await mw.ShowModal(title, Build, 480);
        return picked;
    }

    /// 是／否確認：回傳 true＝使用者按了確定。
    public static async Task<bool> ConfirmAsync(string title, string message, string yes = "確定", string no = "取消") =>
        await ChoiceAsync(title, message, yes, no) == 0;

    /// 單純提示（只有「好」）。
    public static Task InfoAsync(string title, string message) => ChoiceAsync(title, message, "好");
}
