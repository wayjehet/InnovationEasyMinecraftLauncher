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
public record CardItem(string InstanceName, string AccountName, ICommand LaunchCommand);

public partial class MainViewModel : ObservableObject
{
    readonly IInstanceService _instances = new InstanceService();
    readonly IAccountService _accounts = new AccountService();
    readonly ILaunchService _launcher;
    readonly MusicService _music = new();
    readonly EasyTierService _easyTier = new();

    public ObservableCollection<InstanceConfig> Instances { get; } = new();
    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<CardItem> Cards { get; } = new();
    [ObservableProperty] InstanceConfig? selectedInstance;
    [ObservableProperty] Account? selectedAccount;
    [ObservableProperty] string status = "";
    [ObservableProperty] double progressPercent;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] string musicTitle = "（尚未載入音樂）";
    [ObservableProperty] double musicVolume = 0.7;

    public MainViewModel()
    {
        _launcher = new LaunchService(_instances, _accounts);
        _music.Changed += () => Application.Current.Dispatcher.Invoke(() =>
            MusicTitle = _music.Current?.Title ?? $"已載入 {_music.Playlist.Count} 首");
        _ = RefreshAsync();
    }
    partial void OnMusicVolumeChanged(double v) => _music.Volume = (float)v;

    async Task RefreshAsync()
    {
        var keep = SelectedInstance?.Id;
        Instances.Clear(); Accounts.Clear(); Cards.Clear();
        foreach (var a in await _accounts.ListAsync()) Accounts.Add(a);
        foreach (var i in await _instances.ListAsync())
        {
            Instances.Add(i);
            foreach (var c in i.QuickCards)
            {
                var acc = Accounts.FirstOrDefault(a => a.Id == c.AccountId);
                if (acc is null) continue;
                var (iid, aid) = (i.Id, acc.Id);
                Cards.Add(new CardItem(i.Name, acc.Name, new AsyncRelayCommand(() => RunLaunch(iid, aid))));
            }
        }
        SelectedInstance = Instances.FirstOrDefault(i => i.Id == keep);
    }

    partial void OnSelectedInstanceChanged(InstanceConfig? v) =>
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == v?.AccountId);

    partial void OnSelectedAccountChanged(Account? v)
    {
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

    [RelayCommand] async Task OpenAccounts()
    {
        new AccountWindow(_accounts) { Owner = Application.Current.MainWindow }.ShowDialog();
        await RefreshAsync();
    }

    [RelayCommand] void OpenSeedMap() =>
        new SeedMapWindow(SelectedInstance) { Owner = Application.Current.MainWindow }.Show();

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

    [RelayCommand] async Task OpenSettings()
    {
        if (SelectedInstance is null) return;
        var win = new InstanceSettingsWindow(SelectedInstance, Accounts.ToList()) { Owner = Application.Current.MainWindow };
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
