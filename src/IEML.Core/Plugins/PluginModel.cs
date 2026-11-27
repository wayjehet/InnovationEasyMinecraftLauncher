using System.Text.Json;
using System.Text.RegularExpressions;
using IEML.Core.Services;
namespace IEML.Core.Plugins;

/// 插件權限清單。全部預設關閉；插件必須在 manifest 宣告、使用者再逐項勾選才算有效。
public static class Perm
{
    public const string UiPage = "ui.page", UiDialog = "ui.dialog", UiToast = "ui.toast", Hooks = "hooks",
        Storage = "storage", InstancesRead = "instances.read", AccountsRead = "accounts.read",
        TokenRead = "token.read", NetHttp = "net.http", Exec = "exec";

    /// 權限 → (顯示說明, 是否高風險)。
    public static readonly IReadOnlyDictionary<string, (string Text, bool Dangerous)> All = new Dictionary<string, (string, bool)>
    {
        [UiPage] = ("新增頁面（出現在「更多 → 插件」）", false),
        [UiDialog] = ("顯示確認／選擇彈窗（已開放：不用勾選也能使用，保留此權限只為相容舊插件）", false),
        [UiToast] = ("顯示提示條", false),
        [Hooks] = ("接收啟動器事件（例如啟動實例、啟動器就緒）", false),
        [Storage] = ("在自己的資料夾內儲存少量資料（上限 200 KB）", false),
        [InstancesRead] = ("讀取實例清單（名稱、版本、載入器；不含路徑）", false),
        [AccountsRead] = ("讀取帳號清單（名稱、正版／離線；不含 UUID 與權杖）", false),
        [TokenRead] = ("讀取目前選取的正版帳號的存取權杖（能以你的身分登入遊戲）", true),
        [NetHttp] = ("對 manifest 列出的網域發出 HTTPS 請求（GET／POST…、SSE 串流；可能外流你開放給它的資料）", true),
        [Exec] = ("執行插件資料夾內、已核對雜湊的外部程式（以你的 Windows 權限執行，沙盒管不到它）", true),
    };
}

/// 「更多」頁的內建項目標題：插件釘選的入口不能與這些同名（防仿冒）。新增內建項目時請一併更新。
public static class BuiltInTitles
{
    public static readonly IReadOnlyCollection<string> More = new[]
    {
        "伺服器查找", "遊戲日誌", "種子地圖", "地圖管理", "帳號管理", "地城探險", "插件",
        "下載", "P2P 連線", "音樂", "啟動器設定", "陶瓦聯機 Terracotta", "EasyTier 房間",
    };
}

public class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Main { get; set; } = "main.js";
    public List<string> Permissions { get; set; } = new();
    /// net.http 允許的網域（完全相符、只能 HTTPS）。
    public List<string> NetworkHosts { get; set; } = new();

    // ───── 插件 2.0（apiVersion ≥ 2 才能用）─────
    /// 1＝舊版（只有同步 http.get）；2＝非同步 http／SSE／金鑰、exec、contributes。
    public int ApiVersion { get; set; } = 1;
    /// 需要的金鑰（實際金鑰由使用者在插件設定頁填入，由主程式保管，插件讀不到）。
    public List<SecretDecl> Secrets { get; set; } = new();
    /// 允許執行的外部程式（需要 exec 權限）。
    public List<ExecDecl> Exec { get; set; } = new();
    /// 想釘選到「更多」頁的入口（需要使用者逐項核准）。
    public ContribDecl Contributes { get; set; } = new();

    static readonly Regex IdRx = new("^[a-z0-9][a-z0-9._-]{1,39}$", RegexOptions.Compiled);
    static readonly Regex HostRx = new("^[a-z0-9]([a-z0-9.-]{0,251}[a-z0-9])?$", RegexOptions.Compiled);

    /// 回傳錯誤訊息；null＝通過。
    public string? Validate()
    {
        if (!IdRx.IsMatch(Id)) return "id 只能用小寫英數、點、底線、連字號（2–40 字，開頭要英數）";
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40) return "name 必填，且不超過 40 字";
        if (Version.Length > 20 || Author.Length > 40 || Description.Length > 300) return "version／author／description 太長";
        if (string.IsNullOrWhiteSpace(Main) || Path.IsPathRooted(Main) || Main.Contains("..") || !Main.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            return "main 必須是插件資料夾內的相對路徑 .js 檔";
        foreach (var p in Permissions) if (!Perm.All.ContainsKey(p)) return $"未知的權限：{p}";
        if (ApiVersion is < 1 or > 2) return "apiVersion 只能是 1 或 2";
        if (ApiVersion >= 2)
        {
            var he = HttpRules.ValidateHostRules(NetworkHosts);     // 2.0：允許 host:port，不能是 IP
            if (he is not null) return he;
        }
        else foreach (var h in NetworkHosts) if (!HostRx.IsMatch(h)) return $"網域格式不正確：{h}（要小寫、不能含萬用字元或路徑）";
        if (Permissions.Contains(Perm.NetHttp) && NetworkHosts.Count == 0) return "宣告了 net.http 就必須列出 networkHosts";

        if (ApiVersion < 2 && (Secrets.Count > 0 || Exec.Count > 0 || Contributes.Pins.Count > 0))
            return "secrets／exec／contributes 需要 apiVersion ≥ 2";
        if (Permissions.Contains(Perm.Exec) && ApiVersion < 2) return "exec 權限需要 apiVersion ≥ 2";

        if (Secrets.Count > 4) return "secrets 最多 4 個";
        var seenSecrets = new HashSet<string>();
        foreach (var sd in Secrets)
        {
            var se = sd.Validate(NetworkHosts);
            if (se is not null) return se;
            if (!seenSecrets.Add(sd.Name)) return $"secret name 重複：{sd.Name}";
        }
        if (Secrets.Count > 0 && !Permissions.Contains(Perm.NetHttp)) return "宣告 secrets 需要 net.http 權限";

        var ee = ExecDecl.ValidateAll(Exec);
        if (ee is not null) return ee;
        if (Exec.Count > 0 && !Permissions.Contains(Perm.Exec)) return "宣告 exec 清單需要 exec 權限";
        if (Permissions.Contains(Perm.Exec) && Exec.Count == 0) return "宣告了 exec 權限就必須在 exec 列出程式";

        var ce = Contributes.Validate(ApiVersion, Permissions, BuiltInTitles.More);
        if (ce is not null) return ce;
        return null;
    }
}

/// 使用者對單一插件的設定（存在 plugins.json）。預設：停用、沒有任何權限。
public class PluginState
{
    public bool Enabled { get; set; }
    public List<string> Granted { get; set; } = new();
    /// 釘選入口的核准紀錄：pinId → 核准當時的內容雜湊（內容一變就失效）。
    public Dictionary<string, string> PinApprovals { get; set; } = new();
    /// exec 的同意紀錄（綁協議版本＋插件版本＋exec 清單雜湊，任何一項變動就失效）。
    public ExecConsent? ExecAgreement { get; set; }
}

public class InstalledPlugin
{
    public PluginManifest Manifest { get; set; } = new();
    public string Dir { get; set; } = "";
    public string? Error { get; set; }
}

public class PluginStore
{
    public static string Dir => Path.Combine(AppPaths.Root, "plugins");
    static string StatePath => Path.Combine(AppPaths.Root, "plugins.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public Dictionary<string, PluginState> States { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(StatePath))
                States = JsonSerializer.Deserialize<Dictionary<string, PluginState>>(File.ReadAllText(StatePath), Opt) ?? new();
        }
        catch { States = new(); }                    // 壞掉就當全部沒授權（最安全的預設）
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(States, Opt));
        File.Move(tmp, StatePath, overwrite: true);
    }

    public PluginState StateOf(string id)
    {
        if (!States.TryGetValue(id, out var s)) States[id] = s = new PluginState();
        return s;
    }

    /// 掃描插件資料夾；每個子資料夾要有 plugin.json，且 manifest.id 必須等於資料夾名稱（避免冒名）。
    public List<InstalledPlugin> Discover()
    {
        var list = new List<InstalledPlugin>();
        Directory.CreateDirectory(Dir);
        foreach (var dir in Directory.GetDirectories(Dir))
        {
            var folder = Path.GetFileName(dir);
            var item = new InstalledPlugin { Dir = dir, Manifest = new PluginManifest { Id = folder, Name = folder } };
            var mf = Path.Combine(dir, "plugin.json");
            try
            {
                if (!File.Exists(mf)) continue;
                if (new FileInfo(mf).Length > 64 * 1024) throw new InvalidDataException("plugin.json 太大");
                var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(mf), Opt) ?? throw new InvalidDataException("plugin.json 是空的");
                item.Manifest = m;
                item.Error = m.Validate();
                if (item.Error is null && m.Id != folder) item.Error = $"manifest 的 id（{m.Id}）必須和資料夾名稱（{folder}）相同";
                m.Permissions = m.Permissions.Distinct().ToList();
            }
            catch (Exception ex) { item.Error = "讀取 plugin.json 失敗：" + ex.Message; }
            list.Add(item);
        }
        return list.OrderBy(p => p.Manifest.Name).ToList();
    }
}
