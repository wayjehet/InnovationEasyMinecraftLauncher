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
    Task<IReadOnlyList<string>> InstallVersionAsync(InstanceConfig? target, ContentHit hit, ContentVersion version,
        bool installRequired, IReadOnlyCollection<string> optionalProjects, CancellationToken ct = default);
}
