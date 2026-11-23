using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// 下載頁「內容」分頁：點搜尋結果 → 項目詳情頁（所有版本，目前只支援 Modrinth）。
public partial class DownloadStageControl
{
    bool _syncing;

    void InitDetail()
    {
        Detail.BackRequested += () =>
        {
            Detail.Visibility = Visibility.Collapsed;
            Motion.FadeSlideIn(ResultsList, -10, 180);
        };
        // 「安裝到」在內容分頁和遊戲分頁各有一個下拉（遊戲分頁那個在內容分頁看不到），兩邊同步
        InstanceBox.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            try { TargetBox.SelectedItem = InstanceBox.SelectedItem; } finally { _syncing = false; }
        };
    }

    void TargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        try { InstanceBox.SelectedItem = TargetBox.SelectedItem; } finally { _syncing = false; }
    }

    async void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ContentHit hit) return;
        ResultsList.SelectedItem = null; // 清掉選取，返回後可再點同一項
        var source = (SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Modrinth";
        IVersionSource? src = source == "CurseForge" ? _curseForge : _modrinth as IVersionSource;
        if (src is null) return;
        Detail.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(Detail, 10, 200);
        await Detail.ShowAsync(src, hit, InstanceBox.SelectedItem as InstanceConfig);
    }
}
