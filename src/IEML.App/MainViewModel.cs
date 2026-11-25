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
    public const double CardWidth = 210, CardHeight = 104;
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

    partial void OnMusicVolumeChanged(double value) => _music.Volume = (float)value;

    /// 給設置頁（實例目錄變更後重新掃描實例清單）。
    public Task RefreshInstancesPublicAsync() => RefreshAsync();

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
                    X = c.X ?? (k == 1 ? 268 : k == 2 ? 146 : 24),
                    Y = c.Y ?? (k == 0 ? 30 : k == 1 ? 44 : k == 2 ? 190 : 60 + k * 14)
                });
            }
        }
        SelectedInstance = Instances.FirstOrDefault(i => i.Id == keep) ?? Instances.FirstOrDefault();
        ClampCards();
    }

    // 使用者「目前選的帳號」：換實例時以它為準（不被新實例舊的綁定蓋掉），重新整理清單時也靠它還原
    Guid? _currentAccountId;

    partial void OnSelectedInstanceChanged(InstanceConfig? value)
    {
        if (value is null) { SelectedAccount = null; return; }
        var wanted = _currentAccountId is { } cur ? Accounts.FirstOrDefault(a => a.Id == cur) : null;
        // 還沒選過帳號（剛開啟）才退回該實例原本綁的帳號，再不行用第一個
        wanted ??= Accounts.FirstOrDefault(a => a.Id == value.AccountId) ?? Accounts.FirstOrDefault();
        SelectedAccount = wanted; // 變更時 OnSelectedAccountChanged 會把它綁到新實例
    }

    partial void OnSelectedAccountChanged(Account? value)
    {
        if (value is not null) _currentAccountId = value.Id; // 清單重新整理造成的 null 不算使用者的選擇
        // 正版帳號用皮膚頭像（第三方服務 mc-heads.net）；離線帳號沒有，介面會顯示預設圖
        AvatarUrl = value is { Type: AccountType.Microsoft, Uuid.Length: > 0 } ? $"https://mc-heads.net/avatar/{value.Uuid}/128" : null;
        if (value is null || SelectedInstance is null || SelectedInstance.AccountId == value.Id) return;
        _ = _instances.BindAccountAsync(SelectedInstance.Id, value.Id);
        SelectedInstance.AccountId = value.Id;
    }

    [RelayCommand] async Task AddInstance()
    {
        if (Application.Current.MainWindow is not MainWindow mw) return;
        NewInstancePanel? panel = null;
        var name = $"實例 {Instances.Count + 1}";
        if (!await mw.ShowModal("新增實例", close => panel = new NewInstancePanel(name, close), 380) || panel is null) return;
        InstanceRoots.SetActive(panel.RootPath);   // 新實例建立在選的目錄（並記住，下次預設用它）
        await _instances.CreateAsync(new InstanceConfig { Name = panel.InstanceName, McVersion = panel.McVersion, Loader = panel.Loader });
        await RefreshAsync();
    }

    [RelayCommand] async Task DeleteInstance()
    {
        if (SelectedInstance is null) return;
        if (!await Dialogs.ConfirmAsync("刪除實例", $"確定刪除實例「{SelectedInstance.Name}」？存檔與模組也會一併刪除。", "刪除")) return;
        if (SelectedInstance is null) return;
        await _instances.DeleteAsync(SelectedInstance.Id);
        SelectedInstance = null;
        await RefreshAsync();
    }

    [RelayCommand] async Task DeleteAccount()
    {
        if (SelectedAccount is null) return;
        if (!await Dialogs.ConfirmAsync("刪除帳號", $"確定刪除帳號「{SelectedAccount.Name}」？", "刪除")) return;
        if (SelectedAccount is null) return;
        await _accounts.RemoveAsync(SelectedAccount.Id);
        await RefreshAsync();
    }

    [RelayCommand] async Task OpenContent()
    {
        await ShowPage("模組與內容 (Modrinth)", "搜尋、安裝與管理模組、光影、資源包、整合包", new ContentView(SelectedInstance, new ModrinthService(_instances)));
        await RefreshAsync();
    }

    /// 在主畫面的覆蓋頁顯示頁面（取代彈出視窗）；回傳的 Task 在頁面關閉時完成。
    static Task ShowPage(string title, string footer, System.Windows.UIElement page, bool scroll = false) =>
        (Application.Current.MainWindow as MainWindow)?.ShowPage(title, footer, page, scroll: scroll) ?? Task.CompletedTask;

    [RelayCommand] Task OpenServers() =>
        ShowPage("伺服器查找", "收藏伺服器並查詢即時狀態", new ServerPage());

    [RelayCommand] Task OpenLog() =>
        ShowPage("遊戲日誌", "遊戲輸出與啟動錯誤", new LogPage());

    [RelayCommand] async Task OpenCurseForge()
    {
        await ShowPage("CurseForge 內容與整合包", "搜尋並安裝 CurseForge 內容", new CurseForgePage(_instances, SelectedInstance));
        await RefreshAsync();
    }


    // ---------- 自動更新 ----------
    [RelayCommand] Task CheckUpdate() => RunUpdateCheckAsync(manual: true);

    /// 啟動時呼叫：有開啟自動檢查、且距離上次檢查超過 12 小時才會去問 GitHub。
    public Task AutoCheckUpdateAsync() =>
        UpdateSettings.AutoCheck && DateTime.UtcNow - UpdateSettings.LastCheckUtc > TimeSpan.FromHours(12)
            ? RunUpdateCheckAsync(manual: false) : Task.CompletedTask;

    async Task RunUpdateCheckAsync(bool manual)
    {
        try
        {
            UpdateService.CleanupOld();
            if (manual) Status = "檢查更新中…";
            var up = await UpdateService.CheckAsync(AppInfo.Version);
            UpdateSettings.MarkChecked();
            if (up is null) { Status = manual ? "目前已是最新版本" : "待命"; return; }
            if (!manual && UpdateSettings.SkipTag == up.Tag) return;

            var notes = up.Notes.Length > 500 ? up.Notes[..500] + "…" : up.Notes;
            var ans = await Dialogs.ChoiceAsync("有新版本",
                $"發現新版本 {up.Tag}（目前 {AppInfo.Version}）\n\n{notes}\n\n" +
                $"（會驗證檔案雜湊{(UpdateService.SignatureEnabled ? "與簽章" : "")}；更新時會關閉並重新啟動啟動器，請先關閉遊戲。）",
                "下載並更新", "稍後", "略過這個版本");
            if (ans == 2) { UpdateSettings.SkipTag = up.Tag; return; }
            if (ans != 0) return;

            IsBusy = true; Status = "下載更新…"; ProgressPercent = 0;
            var dir = await UpdateService.DownloadAndVerifyAsync(up, new Progress<double>(p => ProgressPercent = p));
            Status = "驗證完成，重新啟動中…";
            UpdateService.ApplyAndRestart(dir);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            GameLog.Append("更新失敗：" + ex.Message);
            if (manual) Status = "更新失敗：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] Task OpenMaps()
    {
        if (SelectedInstance is null) { Status = "請先選一個實例（地圖會放進這個實例的 saves 資料夾）"; return Task.CompletedTask; }
        return ShowPage("地圖管理", "匯入、備份與瀏覽地圖", new MapsPage(_instances, SelectedInstance));
    }

    [RelayCommand] async Task OpenAccounts()
    {
        await ShowPage("帳號管理", "離線／Microsoft 帳號與重新命名", new AccountPage(_accounts));
        await RefreshAsync();
    }

    [RelayCommand] async Task OpenLauncherSettings()
    {
        await ShowPage("啟動器設定", "配色、素材與更新", new LauncherSettingsPage(this), scroll: true);
        await RefreshAsync();
    }

    [RelayCommand] Task OpenInstancePicker() =>
        (Application.Current.MainWindow as MainWindow)?.ShowModal("選擇實例", close => new InstancePickerPanel(this, close), 420) ?? Task.CompletedTask;

    [RelayCommand] Task OpenSeedMap() =>
        ShowPage("種子地圖", "結構與群系座標查詢", new SeedMapPage(SelectedInstance));

    [RelayCommand] Task OpenTerracotta() =>
        ShowPage("P2P 連線（陶瓦聯機）", "開房、加入房間與查看連線狀態", new TerracottaPage(_terracotta), scroll: true);

    /// 啟動器關閉時結束陶瓦行程
    public void Shutdown() => _terracotta.StopBlocking();

    [RelayCommand] Task OpenRoom() =>
        ShowPage("EasyTier 房間", "進階虛擬區域網路連線", new RoomPage(_easyTier), scroll: true);

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
        if (Application.Current.MainWindow is not MainWindow mw) return;
        var (saved, server) = await mw.ShowInstanceSettingsPage(inst, Accounts.ToList());
        if (saved) await _instances.SaveAsync(inst);
        await RefreshAsync();
        if (saved && server is not null) await RunLaunch(inst.Id, SelectedAccount?.Id, server);   // 伺服器分頁的「啟動並加入」
    }

    /// 給插件系統（token.read 權限）用：目前選的正版帳號的存取權杖；離線帳號、失敗或逾時回傳 null。
    /// 在背景執行緒取得、最多等 10 秒（若剛好需要互動登入，視窗可能要等逾時後才出現——原型的限制）。
    public string? GetSelectedAccountToken()
    {
        var acc = SelectedAccount;
        if (acc is null || acc.Type != AccountType.Microsoft) return null;
        var task = Task.Run(() => _accounts.GetSessionAsync(acc.Id));
        return task.Wait(TimeSpan.FromSeconds(10)) ? task.Result.AccessToken : null;
    }

    /// 啟動鍵旁 ▾ 選的伺服器：啟動並直接加入（只這一次，不改設定）。
    [RelayCommand] Task LaunchToServer(string address) =>
        SelectedInstance is null || string.IsNullOrWhiteSpace(address)
            ? Task.CompletedTask : RunLaunch(SelectedInstance.Id, SelectedAccount?.Id, address);

    /// 用目前選的帳號為目前實例新增一張快捷卡片（位置由預設排列決定，之後可拖動）。
    [RelayCommand] async Task AddCard()
    {
        var inst = SelectedInstance;
        if (inst is null) { Status = "請先在左邊清單選一個實例（沒有實例的話先按「新增實例」）"; return; }
        var acc = SelectedAccount ?? Accounts.FirstOrDefault();
        if (acc is null) { Status = "請先建立帳號，才能新增快捷卡片"; return; }
        inst.QuickCards.Add(new QuickLaunchCard { AccountId = acc.Id });
        await _instances.SaveAsync(inst);
        await RefreshAsync();
        Status = $"已新增卡片：{acc.DisplayName}";
    }

    [RelayCommand] Task Launch() =>
        SelectedInstance is null ? Task.CompletedTask : RunLaunch(SelectedInstance.Id, SelectedAccount?.Id); // 以畫面上目前選的帳號啟動，不依賴磁碟上的綁定

    async Task RunLaunch(Guid instanceId, Guid? accountOverride, string? server = null)
    {
        if (IsBusy) { Status = "正在啟動中，請稍候…"; return; }
        // 插件事件：只給名稱、版本、載入器（有 hooks 權限且訂閱了的插件才會收到）
        if (Instances.FirstOrDefault(i => i.Id == instanceId) is { } launching)
            (Application.Current.MainWindow as MainWindow)?.Plugins.Emit("instance.launch", new System.Text.Json.Nodes.JsonObject
            { ["name"] = launching.Name, ["mcVersion"] = launching.McVersion, ["loader"] = launching.Loader.ToString() });
        IsBusy = true; ProgressPercent = 0;
        try
        {
            var prog = new Progress<(string Stage, double Percent)>(p =>
            { Status = $"{p.Stage} {p.Percent:0}%"; ProgressPercent = Math.Clamp(p.Percent, 0, 100); });
            var pid = await _launcher.LaunchAsync(instanceId, prog, default, accountOverride, server);
            Status = $"已啟動 (PID {pid})";
        }
        catch (Exception ex) { Status = "錯誤：" + ex.Message; }
        finally { IsBusy = false; ProgressPercent = 0; }
    }
}
