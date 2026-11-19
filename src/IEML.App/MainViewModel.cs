using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

/// A quick-launch card on the main screen: one instance + one specific account.
public partial class CardItem : ObservableObject
{
    public Guid InstanceId { get; init; }
    public Guid CardId { get; init; }
    public string InstanceName { get; init; } = "";
    public string AccountName { get; init; } = "";
    public ICommand LaunchCommand { get; init; } = null!;
    public ICommand SettingsCommand { get; init; } = null!;
    public string Initial => string.IsNullOrEmpty(AccountName) ? "?" : AccountName[..1].ToUpperInvariant();
    /// 在卡片畫布上的位置（拖拉時即時更新）
    [ObservableProperty] double x;
    [ObservableProperty] double y;
    /// 重疊時的前後順序（按下卡片就拉到最上層，只影響畫面，不存檔）
    [ObservableProperty] int z;
}

public partial class MainViewModel : ObservableObject
{
    readonly IInstanceService _instances = new InstanceService();
    readonly IAccountService _accounts = new AccountService();
    readonly ILaunchService _launcher;
    readonly MusicService _music = new();
    readonly EasyTierService _easyTier = new();
    readonly TerracottaService _terracotta = new();

    public ObservableCollection<InstanceConfig> Instances { get; } = new();
    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<CardItem> Cards { get; } = new();
    [ObservableProperty] InstanceConfig? selectedInstance;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AvatarInitial))] Account? selectedAccount;
    [ObservableProperty] string status = "待命";
    [ObservableProperty] double progressPercent;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] string musicTitle = "（尚未載入音樂）";
    [ObservableProperty] double musicVolume = 0.7;
    [ObservableProperty] string? avatarUrl;

    public string AvatarInitial => string.IsNullOrEmpty(SelectedAccount?.Name) ? "?" : SelectedAccount!.Name[..1].ToUpperInvariant();

    public MainViewModel()
    {
        _launcher = new LaunchService(_instances, _accounts);
        _music.Changed += () => Application.Current.Dispatcher.Invoke(() =>
            MusicTitle = _music.Current?.Title ?? $"已載入 {_music.Playlist.Count} 首");
        _ = RefreshAsync();
    }
    // ---------- 快捷啟動卡片：自由拖拉擺放 ----------
    public const double CardWidth = 220, CardHeight = 104;
    double _cardsW, _cardsH;

    /// 畫布大小改變時由視窗通知；把跑出畫面的卡片拉回來（只改畫面，不改已存的位置）
    public void SetCardsArea(double w, double h) { _cardsW = w; _cardsH = h; ClampCards(); }

    void ClampCards()
    {
        if (_cardsW <= 0 || _cardsH <= 0) return;
        foreach (var c in Cards)
        {
            c.X = Math.Clamp(c.X, 0, Math.Max(0, _cardsW - CardWidth));
            c.Y = Math.Clamp(c.Y, 0, Math.Max(0, _cardsH - CardHeight));
        }
    }

    public async Task SaveCardPositionAsync(CardItem c)
    {
        var inst = Instances.FirstOrDefault(i => i.Id == c.InstanceId);
        var q = inst?.QuickCards.FirstOrDefault(x => x.Id == c.CardId);
        if (inst is null || q is null) return;
        q.X = c.X; q.Y = c.Y;
        await _instances.SaveAsync(inst);
    }

    [RelayCommand] async Task ResetCardLayout()
    {
        foreach (var i in Instances)
        {
            bool changed = false;
            foreach (var q in i.QuickCards)
                if (q.X != null || q.Y != null) { q.X = null; q.Y = null; changed = true; }
            if (changed) await _instances.SaveAsync(i);
        }
        await RefreshAsync();
    }

    partial void OnMusicVolumeChanged(double v) => _music.Volume = (float)v;

    async Task RefreshAsync()
    {
        var keep = SelectedInstance?.Id;
        Instances.Clear(); Accounts.Clear(); Cards.Clear();
        foreach (var a in await _accounts.ListAsync()) Accounts.Add(a);
        int n = 0;
        foreach (var i in await _instances.ListAsync())
        {
            Instances.Add(i);
            foreach (var c in i.QuickCards)
            {
                var acc = Accounts.FirstOrDefault(a => a.Id == c.AccountId);
                if (acc is null) continue;
                var (iid, aid) = (i.Id, acc.Id);
                int k = n++;
                Cards.Add(new CardItem
                {
                    InstanceId = i.Id, CardId = c.Id, InstanceName = i.Name, AccountName = acc.Name,
                    LaunchCommand = new AsyncRelayCommand(() => RunLaunch(iid, aid)),
                    SettingsCommand = new AsyncRelayCommand(() => OpenSettingsFor(iid)),
                    // 還沒擺過：預設照草圖——上排兩張（右邊略低）、第三張置中在下，之後的往下排
                    X = c.X ?? (k == 2 ? (CardWidth + 10) / 2.0 : (k % 2) * (CardWidth + 10)),
                    Y = c.Y ?? (k / 2) * (CardHeight + 10) + (k % 2 == 1 ? 14 : 0)
                });
            }
        }
        SelectedInstance = Instances.FirstOrDefault(i => i.Id == keep) ?? Instances.FirstOrDefault();
        ClampCards();
    }

    partial void OnSelectedInstanceChanged(InstanceConfig? v) =>
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == v?.AccountId);

    partial void OnSelectedAccountChanged(Account? v)
    {
        // 正版帳號用皮膚頭像（第三方服務 mc-heads.net）；離線帳號沒有，介面會顯示預設圖
        AvatarUrl = v is { Type: AccountType.Microsoft, Uuid.Length: > 0 } ? $"https://mc-heads.net/avatar/{v.Uuid}/128" : null;
        if (v is null || SelectedInstance is null || SelectedInstance.AccountId == v.Id) return;
        _ = _instances.BindAccountAsync(SelectedInstance.Id, v.Id);
        SelectedInstance.AccountId = v.Id;
    }

    [RelayCommand] async Task AddInstance()
    {
        var win = new NewInstanceWindow($"實例 {Instances.Count + 1}") { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() != true) return;
        await _instances.CreateAsync(new InstanceConfig { Name = win.InstanceName, McVersion = win.McVersion, Loader = win.Loader });
        await RefreshAsync();
    }

    [RelayCommand] async Task DeleteInstance()
    {
        if (SelectedInstance is null) return;
        if (MessageBox.Show($"確定刪除實例「{SelectedInstance.Name}」？存檔與模組也會一併刪除。", "刪除實例",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _instances.DeleteAsync(SelectedInstance.Id);
        SelectedInstance = null;
        await RefreshAsync();
    }

    [RelayCommand] async Task DeleteAccount()
    {
        if (SelectedAccount is null) return;
        if (MessageBox.Show($"確定刪除帳號「{SelectedAccount.Name}」？", "刪除帳號",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _accounts.RemoveAsync(SelectedAccount.Id);
        await RefreshAsync();
    }

    [RelayCommand] async Task OpenContent()
    {
        new ContentWindow(SelectedInstance, new ModrinthService(_instances)) { Owner = Application.Current.MainWindow }.ShowDialog();
        await RefreshAsync();
    }

    [RelayCommand] void OpenServers() =>
        new ServerWindow { Owner = Application.Current.MainWindow }.Show();

    [RelayCommand] void OpenLog() =>
        new LogWindow { Owner = Application.Current.MainWindow }.Show();

    [RelayCommand] async Task OpenCurseForge()
    {
        new CurseForgeWindow(_instances, SelectedInstance) { Owner = Application.Current.MainWindow }.ShowDialog();
        await RefreshAsync();
    }

    [RelayCommand] void OpenMaps()
    {
        if (SelectedInstance is null) { Status = "請先選一個實例（地圖會放進這個實例的 saves 資料夾）"; return; }
        new MapsWindow(_instances, SelectedInstance) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand] async Task OpenAccounts()
    {
        new AccountWindow(_accounts) { Owner = Application.Current.MainWindow }.ShowDialog();
        await RefreshAsync();
    }

    [RelayCommand] async Task OpenLauncherSettings()
    {
        new LauncherSettingsWindow(this) { Owner = Application.Current.MainWindow }.ShowDialog();
        await RefreshAsync();
    }

    [RelayCommand] void OpenInstancePicker() =>
        new InstancePickerWindow(this) { Owner = Application.Current.MainWindow }.ShowDialog();

    [RelayCommand] void OpenSeedMap() =>
        new SeedMapWindow(SelectedInstance) { Owner = Application.Current.MainWindow }.Show();

    [RelayCommand] void OpenTerracotta() =>
        new TerracottaWindow(_terracotta) { Owner = Application.Current.MainWindow }.Show();

    /// 啟動器關閉時結束陶瓦行程
    public void Shutdown() => _terracotta.StopBlocking();

    [RelayCommand] void OpenRoom() =>
        new RoomWindow(_easyTier) { Owner = Application.Current.MainWindow }.Show();

    [RelayCommand] void AddMusicFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() == true) _music.AddFolder(dlg.FolderName);
    }
    [RelayCommand] void PlayMusic() => _music.Play();
    [RelayCommand] void PauseMusic() => _music.Pause();
    [RelayCommand] void NextMusic() => _music.Next();
    [RelayCommand] void PrevMusic() => _music.Previous();

    [RelayCommand] async Task AddMicrosoftAccount()
    {
        try
        {
            Status = "請在彈出的視窗登入 Microsoft 帳號…";
            var acc = await _accounts.AddMicrosoftAsync();
            await RefreshAsync();
            Status = $"已新增正版帳號：{acc.Name}";
        }
        catch (Exception ex) { Status = "登入失敗：" + ex.Message; }
    }

    [RelayCommand] async Task AddOfflineAccount()
    {
        await _accounts.AddOfflineAsync($"Player{Accounts.Count + 1}");
        await RefreshAsync();
    }

    [RelayCommand] Task OpenSettings()
    {
        if (SelectedInstance is null) { Status = "請先在左邊清單選一個實例（沒有實例的話先按「新增實例」）"; return Task.CompletedTask; }
        return OpenSettingsFor(SelectedInstance.Id);
    }

    async Task OpenSettingsFor(Guid instanceId)
    {
        var inst = Instances.FirstOrDefault(i => i.Id == instanceId);
        if (inst is null) return;
        var win = new InstanceSettingsWindow(inst, Accounts.ToList()) { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() == true) await _instances.SaveAsync(win.Config);
        await RefreshAsync();
    }

    [RelayCommand] Task Launch() =>
        SelectedInstance is null ? Task.CompletedTask : RunLaunch(SelectedInstance.Id, null);

    async Task RunLaunch(Guid instanceId, Guid? accountOverride)
    {
        if (IsBusy) { Status = "正在啟動中，請稍候…"; return; }
        IsBusy = true; ProgressPercent = 0;
        try
        {
            var prog = new Progress<(string Stage, double Percent)>(p =>
            { Status = $"{p.Stage} {p.Percent:0}%"; ProgressPercent = Math.Clamp(p.Percent, 0, 100); });
            var pid = await _launcher.LaunchAsync(instanceId, prog, default, accountOverride);
            Status = $"已啟動 (PID {pid})";
        }
        catch (Exception ex) { Status = "錯誤：" + ex.Message; }
        finally { IsBusy = false; ProgressPercent = 0; }
    }
}
