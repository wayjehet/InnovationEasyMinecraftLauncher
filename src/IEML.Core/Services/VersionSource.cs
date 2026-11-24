using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// 「項目詳情頁」用的版本來源：列出一個專案的所有版本，並安裝指定的一個。Modrinth、CurseForge 各自實作。
public interface IVersionSource
{
    /// 列出版本（新→舊）。matchTarget＝只列出符合 target 實例（MC 版本＋模組載入器）的版本；整合包一律不篩選。
    Task<IReadOnlyList<ContentVersion>> ListVersionsAsync(ContentHit hit, InstanceConfig? target, bool matchTarget, CancellationToken ct = default);
    /// 專案 id → 名稱（前置模組顯示用）。
    Task<IReadOnlyDictionary<string, string>> GetProjectTitlesAsync(IEnumerable<string> ids, CancellationToken ct = default);
    /// 安裝指定版本；回傳警告清單。整合包建立新實例（target 可為 null），其他類型必須有 target。
    /// pinnedVersions：前置模組專案 id → 指定的版本（Modrinth 版本 id／CurseForge 檔案 id）；沒列在裡面的前置自動挑最新相容版。
    Task<IReadOnlyList<string>> InstallVersionAsync(InstanceConfig? target, ContentHit hit, ContentVersion version,
        bool installRequired, IReadOnlyCollection<string> optionalProjects, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? pinnedVersions = null);

    /// 專案的完整說明（已轉成純文字，最多約 2000 字）；取不到回傳 null。
    Task<string?> GetLongDescriptionAsync(ContentHit hit, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

/// 把 Modrinth 的 Markdown／CurseForge 的 HTML 說明轉成可以直接顯示的純文字。
public static class DescriptionText
{
    public static string Clean(string? raw, int max = 2000)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = raw.Replace("\r", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<(script|style)[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<(br|/p|/div|/li|/h[1-6])\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<li[^>]*>", "• ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<[^>]+>", "");
        t = System.Net.WebUtility.HtmlDecode(t);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"!\[[^\]]*\]\([^)]*\)", "");                 // 圖片
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\[([^\]]*)\]\([^)]*\)", "$1");               // 連結只留文字
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s{0,3}#{1,6}\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*[-*+]\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);
        t = t.Replace("**", "").Replace("__", "").Replace("```", "").Replace("`", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[ \t]+\n", "\n");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
        return t.Length > max ? t[..max].TrimEnd() + "…" : t;
    }
}
