namespace IEML.Core.Services;

/// 下載與整合包安裝的安全規則：網域白名單、可執行檔攔截。
public static class DownloadPolicy
{
    // 允許的網域（含子網域）。轉址後的最終網址也要通過。
    static readonly string[] Hosts =
        { "forgecdn.net", "curseforge.com", "modrinth.com", "github.com", "githubusercontent.com", "gitlab.com" };
    // Modrinth .mrpack 規格允許的下載網域（比一般白名單更窄）
    static readonly string[] MrpackHosts = { "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com" };

    static bool HostIn(Uri u, string[] list) =>
        u.Scheme == Uri.UriSchemeHttps &&
        list.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    public static void Require(Uri u)
    {
        if (!HostIn(u, Hosts)) throw new InvalidDataException($"下載網址不在允許的網域內（只允許 https 的 CurseForge／Modrinth／GitHub／GitLab）：{u.Host}");
    }

    public static void RequireMrpack(Uri u)
    {
        if (!HostIn(u, MrpackHosts)) throw new InvalidDataException($"整合包的下載網域不符合 Modrinth 規格：{u.Host}");
    }

    // 整合包 overrides 裡一律不安裝的可執行／腳本類型（.js 不在內：KubeJS 等模組的設定本來就是 .js）
    static readonly string[] BlockedExt =
        { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".wsf", ".msi", ".scr", ".com", ".lnk", ".hta", ".reg" };

    public static bool IsBlockedFile(string relPath) =>
        BlockedExt.Contains(Path.GetExtension(relPath).ToLowerInvariant());

    public static void ReportPack(string pack, IReadOnlyList<string> blocked, IReadOnlyList<string> jars)
    {
        if (blocked.Count > 0)
            GameLog.Append($"⚠ 整合包「{pack}」含有可執行檔，已略過不安裝：{string.Join("、", blocked.Take(10))}{(blocked.Count > 10 ? "…" : "")}");
        if (jars.Count > 0)
            GameLog.Append($"⚠ 整合包「{pack}」自帶 {jars.Count} 個不在下載清單內的 .jar（來源無法驗證，請確認整合包可信）：{string.Join("、", jars.Take(10))}{(jars.Count > 10 ? "…" : "")}");
    }
}
