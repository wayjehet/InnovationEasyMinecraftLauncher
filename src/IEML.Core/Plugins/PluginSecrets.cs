using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IEML.Core.Services;
namespace IEML.Core.Plugins;

/// 插件金鑰保管庫：實際金鑰由使用者在插件設定頁填入，用 DPAPI（目前使用者）加密後存在
/// %AppData%\InnovationEasyLauncher\plugin-secrets.json。插件只能「指名」金鑰（manifest.secrets 的 name），
/// 讀不到內容；主程式送出請求時才把它注入 header（見 PluginHttpClient），且只能送往金鑰綁定的網域。
public sealed class PluginSecretVault : ISecretVault
{
    readonly Func<string, PluginManifest?> _manifestOf;
    readonly object _gate = new();
    Dictionary<string, string>? _data;     // "pluginId/name" → base64(DPAPI(value))

    static string FilePath => Path.Combine(AppPaths.Root, "plugin-secrets.json");
    static string Key(string pluginId, string name) => pluginId + "/" + name;

    public PluginSecretVault(Func<string, PluginManifest?> manifestOf) => _manifestOf = manifestOf;

    Dictionary<string, string> Load()
    {
        if (_data is not null) return _data;
        try { _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { _data = new(); }
        return _data;
    }

    void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public bool Has(string pluginId, string name)
    {
        lock (_gate) return Load().ContainsKey(Key(pluginId, name));
    }

    /// 儲存（value 為空＝清除）。拒絕控制字元（避免 header 注入）與過長的值。
    public void Set(string pluginId, string name, string value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 4000) throw new ArgumentException("金鑰太長（上限 4000 字）");
        if (value.Any(char.IsControl)) throw new ArgumentException("金鑰不能包含控制字元或換行");
        lock (_gate)
        {
            var d = Load();
            if (value.Length == 0) d.Remove(Key(pluginId, name));
            else d[Key(pluginId, name)] = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            Save();
        }
    }

    public SecretEntry? Get(string pluginId, string name)
    {
        var decl = _manifestOf(pluginId)?.Secrets.FirstOrDefault(s => s.Name == name);
        if (decl is null) return null;                        // 沒在 manifest 宣告過的金鑰一律不給
        string? raw;
        lock (_gate) Load().TryGetValue(Key(pluginId, name), out raw);
        if (raw is null) return null;
        try
        {
            var value = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(raw), null, DataProtectionScope.CurrentUser));
            return new SecretEntry { Name = name, Host = decl.Host, HeaderName = decl.Header, Prefix = decl.Prefix, Value = value };
        }
        catch { return null; }                                // 解不開（換了使用者或檔案壞了）＝當作沒設定
    }
}
