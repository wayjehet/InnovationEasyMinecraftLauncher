using System.Text.Json;
using System.Text.RegularExpressions;
using IEML.Core.Services;
namespace IEML.Core.Plugins;

/// 插件權限清單。全部預設關閉；插件必須在 manifest 宣告、使用者再逐項勾選才算有效。
public static class Perm
{
    public const string UiPage = "ui.page", UiDialog = "ui.dialog", UiToast = "ui.toast", Hooks = "hooks",
        Storage = "storage", InstancesRead = "instances.read", AccountsRead = "accounts.read",
        TokenRead = "token.read", NetHttp = "net.http";

    /// 權限 → (顯示說明, 是否高風險)。
    public static readonly IReadOnlyDictionary<string, (string Text, bool Dangerous)> All = new Dictionary<string, (string, bool)>
    {
        [UiPage] = ("新增頁面（出現在「更多 → 插件」）", false),
        [UiDialog] = ("顯示確認／選擇彈窗", false),
        [UiToast] = ("顯示提示條", false),
        [Hooks] = ("接收啟動器事件（例如啟動實例、啟動器就緒）", false),
        [Storage] = ("在自己的資料夾內儲存少量資料（上限 200 KB）", false),
        [InstancesRead] = ("讀取實例清單（名稱、版本、載入器；不含路徑）", false),
        [AccountsRead] = ("讀取帳號清單（名稱、正版／離線；不含 UUID 與權杖）", false),
        [TokenRead] = ("讀取目前選取的正版帳號的存取權杖（能以你的身分登入遊戲）", true),
        [NetHttp] = ("對 manifest 列出的網域發出 HTTPS GET 請求（可能外流你開放給它的資料）", true),
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
        foreach (var h in NetworkHosts) if (!HostRx.IsMatch(h)) return $"網域格式不正確：{h}（要小寫、不能含萬用字元或路徑）";
        if (Permissions.Contains(Perm.NetHttp) && NetworkHosts.Count == 0) return "宣告了 net.http 就必須列出 networkHosts";
        return null;
    }
}

/// 使用者對單一插件的設定（存在 plugins.json）。預設：停用、沒有任何權限。
public class PluginState
{
    public bool Enabled { get; set; }
    public List<string> Granted { get; set; } = new();
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
