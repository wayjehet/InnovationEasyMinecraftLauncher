namespace IEML.Core.Plugins;

public sealed class PluginDeniedException : Exception
{
    public PluginDeniedException(string message) : base(message) { }
}

/// ★ 插件系統唯一的權限閘口 ★
/// 插件能做的每一件事，都必須是下面 Methods 表裡的一項，而且每次呼叫都經過 Authorize。
/// 規則（預設拒絕）：
///   1. 插件必須已啟用；
///   2. 方法必須在表中（不在表中＝拒絕）；
///   3. 該方法需要的權限，必須「manifest 有宣告」且「使用者已勾選」。
/// 權限是在主程式這邊、每次呼叫時檢查，不是插件自己檢查，所以插件的程式碼怎麼寫都繞不過去。
/// 要審查沙盒，只需要讀這一個檔案與 PluginRuntime.Handle。
public static class PluginGate
{
    /// 方法 → 需要的權限（null＝不需要權限）。新增任何插件 API，都必須先在這裡登記。
    static readonly Dictionary<string, string?> Methods = new()
    {
        ["log"] = null,
        ["hooks.on"] = Perm.Hooks,
        ["ui.registerPage"] = Perm.UiPage,
        ["ui.openPage"] = Perm.UiPage,
        ["ui.toast"] = Perm.UiToast,
        ["ui.confirm"] = Perm.UiDialog,
        ["ui.info"] = Perm.UiDialog,
        ["ui.choose"] = Perm.UiDialog,
        ["storage.get"] = Perm.Storage,
        ["storage.set"] = Perm.Storage,
        ["storage.remove"] = Perm.Storage,
        ["instances.list"] = Perm.InstancesRead,
        ["accounts.list"] = Perm.AccountsRead,
        ["account.getToken"] = Perm.TokenRead,
        ["http.get"] = Perm.NetHttp,
    };

    /// 這個插件現在是否真的擁有某權限（宣告 ∩ 已授權 ∩ 已啟用）。
    public static bool Has(PluginManifest manifest, PluginState state, string perm) =>
        state.Enabled && manifest.Permissions.Contains(perm) && state.Granted.Contains(perm);

    public static void Authorize(PluginManifest manifest, PluginState state, string method)
    {
        if (!state.Enabled) throw new PluginDeniedException("插件未啟用");
        if (!Methods.TryGetValue(method, out var need)) throw new PluginDeniedException($"未知的方法：{method}");
        if (need is null) return;
        if (!manifest.Permissions.Contains(need)) throw new PluginDeniedException($"插件沒有在 manifest 宣告權限 {need}");
        if (!state.Granted.Contains(need)) throw new PluginDeniedException($"使用者尚未授權：{need}（到「更多 → 插件」勾選）");
    }
}
