using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

public partial class InstanceSettingsWindow : Window
{
    public InstanceConfig Config { get; }
    public IReadOnlyList<Account> Accounts { get; }
    public ObservableCollection<QuickLaunchCard> Cards { get; }

    public InstanceSettingsWindow(InstanceConfig cfg, IReadOnlyList<Account> accounts)
    {
        Config = cfg; Accounts = accounts;
        Cards = new(cfg.QuickCards.Select(c => new QuickLaunchCard { Id = c.Id, AccountId = c.AccountId, X = c.X, Y = c.Y }));
        InitializeComponent();
        DataContext = this;
        _origMc = cfg.McVersion;
        LoaderBox.ItemsSource = Enum.GetValues<LoaderType>();
        LoaderBox.SelectedItem = cfg.Loader;   // 會觸發 LoaderBox_Changed → 載入版本清單
    }

    const string AutoText = "自動（最新穩定版）";
    readonly string _origMc;
    CancellationTokenSource? _cts;
    bool _initialLoad = true;

    void LoaderBox_Changed(object s, SelectionChangedEventArgs e)
    {
        if (LoaderVerBox is null) return;
        _ = RefreshLoaderVersionsAsync(resetText: !_initialLoad);
        _initialLoad = false;
    }
    void McBox_LostFocus(object s, RoutedEventArgs e) => _ = RefreshLoaderVersionsAsync(resetText: false);
    void RefreshLoaderVer_Click(object s, RoutedEventArgs e) => _ = RefreshLoaderVersionsAsync(resetText: false);

    LoaderType SelectedLoader => LoaderBox.SelectedItem is LoaderType l ? l : Config.Loader;

    async Task RefreshLoaderVersionsAsync(bool resetText)
    {
        _cts?.Cancel(); _cts = new(); var ct = _cts.Token;
        var loader = SelectedLoader;
        var mc = McBox.Text.Trim();
        // 目前要保留的版本：第一次開窗＝實例存的版本；之後＝使用者目前選的
        var keep = resetText ? AutoText
            : string.IsNullOrWhiteSpace(LoaderVerBox.Text) ? (Config.LoaderVersion ?? AutoText) : LoaderVerBox.Text.Trim();
        if (loader == LoaderType.Vanilla)
        {
            LoaderVerBox.ItemsSource = null; LoaderVerBox.Text = ""; LoaderVerBox.IsEnabled = false;
            LoaderHint.Text = "原版沒有載入器。"; return;
        }
        LoaderVerBox.IsEnabled = true;
        LoaderHint.Text = "載入版本清單中…";
        try
        {
            var list = await LoaderVersionService.ListAsync(loader, mc, ct);
            if (ct.IsCancellationRequested) return;
            var items = new List<string> { AutoText }; items.AddRange(list);
            LoaderVerBox.ItemsSource = items;
            // 原本選的版本不屬於這個 MC 版本／載入器 → 退回自動
            LoaderVerBox.Text = keep == AutoText || list.Contains(keep) ? keep : AutoText;
            LoaderHint.Text = list.Count == 0
                ? $"找不到 {mc} 可用的 {loader} 版本（可能不支援這個 Minecraft 版本）。"
                : $"共 {list.Count} 個版本（穩定版在前）。可直接輸入版本號。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            LoaderVerBox.ItemsSource = new[] { AutoText };
            LoaderVerBox.Text = keep;                       // 離線時保留原值，還是可以手打
            LoaderHint.Text = "無法取得版本清單：" + ex.Message;
        }
    }

    void PickJava_Click(object s, RoutedEventArgs e)
    {
        var w = new JavaPickerWindow(Config.McVersion) { Owner = this };
        if (w.ShowDialog() != true) return;
        Config.JavaPath = w.SelectedPath;
        JavaBox.Text = w.SelectedPath ?? "";
    }

    void AddCard_Click(object s, RoutedEventArgs e) =>
        Cards.Add(new QuickLaunchCard { AccountId = Accounts.FirstOrDefault()?.Id ?? Guid.Empty });

    void RemoveCard_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is QuickLaunchCard c) Cards.Remove(c);
    }

    void Save_Click(object s, RoutedEventArgs e)
    {
        McBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();   // 按 Enter 儲存時，確保 MC 版本已寫回
        var newLoader = SelectedLoader;
        var verText = LoaderVerBox.Text?.Trim();
        string? newVer = newLoader == LoaderType.Vanilla || string.IsNullOrEmpty(verText) || verText == AutoText ? null : verText;

        // 換載入器種類：已安裝的模組通常不能通用，先提醒
        if (newLoader != Config.Loader && Config.Loader != LoaderType.Vanilla)
        {
            var mods = System.IO.Path.Combine(AppPaths.Instances, Config.Id.ToString("N"), "mods");
            if (System.IO.Directory.Exists(mods) && System.IO.Directory.EnumerateFiles(mods, "*.jar").Any() &&
                MessageBox.Show($"從 {Config.Loader} 換成 {newLoader} 後，已安裝的模組多半無法使用，要繼續嗎？\n（模組檔案不會被刪除）",
                    "更換載入器", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }
        // MC 版本改了、但選的載入器版本不在新清單內 → 清成自動（避免用到舊 MC 的載入器版本）
        if (Config.McVersion != _origMc && newVer != null && LoaderVerBox.Items.Count > 0 && !LoaderVerBox.Items.Contains(newVer)) newVer = null;

        Config.Loader = newLoader;
        Config.LoaderVersion = newVer;
        Config.QuickCards = Cards.Where(c => c.AccountId != Guid.Empty).ToList();
        DialogResult = true;
    }

    void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
