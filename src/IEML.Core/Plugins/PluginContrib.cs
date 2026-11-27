// ─────────────────────────────────────────────────────────────────────────────
// PluginContrib.cs ── IEML 插件 2.0「contributes：釘選頁面到『更多』」本體
//
// ★ 獨立檔案：沒有接進專案、沒有改任何現有程式碼，也不依賴專案內其他類別
//   （只用 .NET 8 標準函式庫），放進 IEML.Core/Plugins/ 即可編譯。
//
// 概念：插件只能「提出申請」，不能自己決定出現在哪裡。
//   manifest 宣告想釘選的入口 → 使用者在「更多 → 插件」逐項核准 → 才出現在「更多」頁。
//
// 範例 manifest：
//   "apiVersion": 2,
//   "permissions": ["ui.page"],
//   "contributes": { "pins": [
//       { "id": "chat", "page": "main", "title": "AI 助手", "icon": "🤖", "slot": "more" }
//   ] }
//
// 安全設計：
//   1. 要 apiVersion ≥ 2 並宣告 ui.page；1.0 插件完全不受影響。
//   2. 核准綁定內容雜湊（slot｜page｜title｜icon）：插件更新後改了標題或目標頁，
//      核准自動失效、要重新核准（防「先用好名字騙核准，再改成別的」）。
//   3. 標題不能與內建項目同名；不允許控制字元、雙向文字控制字元、零寬字元（防仿冒／混淆）。
//   4. 顯示一律附上來源：「標題（插件名稱）」，使用者永遠分得出哪個是插件。
//   5. 每個插件最多 3 個、「更多」頁插件入口總數最多 12 個（防洗版）。
//   6. 插件停用、撤銷 ui.page 授權、或頁面不存在時，入口立刻消失。
// ─────────────────────────────────────────────────────────────────────────────
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IEML.Core.Plugins;

// ───────────────────────── manifest 宣告 ─────────────────────────

/// 一個想釘選的入口。
public sealed class PinDecl
{
    public string Id { get; set; } = "";          // 入口代號（同一插件內唯一）
    public string Page { get; set; } = "";        // 要開啟的頁面 id（ieml.ui.registerPage 註冊的）
    public string Title { get; set; } = "";       // 顯示標題，1–20 字
    public string Icon { get; set; } = "";        // 選填：1–2 個表情符號或字元
    public string Slot { get; set; } = "more";    // 目前只有 "more"；之後可擴充

    public static readonly IReadOnlySet<string> AllowedSlots = new HashSet<string> { "more" };

    static readonly Regex IdRx = new("^[a-z0-9][a-z0-9_-]{0,31}$", RegexOptions.Compiled);
    static readonly Regex PageRx = new("^[a-z0-9_-]{1,40}$", RegexOptions.Compiled);   // 與 registerPage 的頁面 id 規則一致

    public string? Validate(IReadOnlyCollection<string> reservedTitles)
    {
        if (!IdRx.IsMatch(Id)) return $"pin id 格式不正確：{Id}";
        if (!PageRx.IsMatch(Page)) return $"pin「{Id}」的 page 格式不正確";
        if (!AllowedSlots.Contains(Slot)) return $"pin「{Id}」的 slot 不支援：{Slot}";

        if (Title.Length is < 1 or > 20 || Title != Title.Trim() || !TextSafety.IsSafe(Title, allowEmojiJoiner: false))
            return $"pin「{Id}」的 title 必須是 1–20 字、前後不能有空白、不能含控制或隱形字元";
        if (reservedTitles.Any(r => string.Equals(r.Trim(), Title, StringComparison.OrdinalIgnoreCase)))
            return $"pin「{Id}」的 title 與內建項目同名：{Title}";

        if (Icon.Length > 0)
        {
            if (Icon.Length > 16 || new StringInfo(Icon).LengthInTextElements > 2 || !TextSafety.IsSafe(Icon, allowEmojiJoiner: true))
                return $"pin「{Id}」的 icon 只能是 1–2 個字元或表情符號";
        }
        return null;
    }

    /// 內容雜湊：核准綁定它，任何一欄變動就會改變。
    public string ContentHash() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Slot}|{Page}|{Title}|{Icon}"))).ToLowerInvariant();
}

public sealed class ContribDecl
{
    public const int MaxPinsPerPlugin = 3;
    public List<PinDecl> Pins { get; set; } = new();

    /// 回傳錯誤訊息；null＝通過。reservedTitles＝內建項目的標題（由主程式提供，用來防仿冒）。
    public string? Validate(int apiVersion, IReadOnlyCollection<string> permissions, IReadOnlyCollection<string> reservedTitles)
    {
        if (Pins.Count == 0) return null;
        if (apiVersion < 2) return "contributes 需要 apiVersion ≥ 2";
        if (!permissions.Contains("ui.page")) return "釘選頁面需要宣告 ui.page 權限";
        if (Pins.Count > MaxPinsPerPlugin) return $"每個插件最多釘選 {MaxPinsPerPlugin} 個入口";

        var seen = new HashSet<string>();
        foreach (var p in Pins)
        {
            var err = p.Validate(reservedTitles);
            if (err is not null) return err;
            if (!seen.Add(p.Id)) return $"pin id 重複：{p.Id}";
        }
        return null;
    }
}

static class TextSafety
{
    /// 擋掉控制字元、行／段分隔符、雙向控制與零寬等「格式字元」（用來偽裝或混淆顯示）。
    /// 表情符號的連接符（ZWJ）只在 icon 允許。
    public static bool IsSafe(string s, bool allowEmojiJoiner)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        foreach (var c in s)
        {
            if (char.IsControl(c)) return false;
            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Format:
                    if (allowEmojiJoiner && c == '\u200D') break;
                    return false;
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                    return false;
            }
        }
        return true;
    }
}

// ───────────────────────── 使用者核准 ─────────────────────────

/// 核准紀錄的操作。核准表是 pinId → 內容雜湊，存進該插件的 PluginState（例如 Dictionary<string,string> PinApprovals）。
public static class PinPolicy
{
    public static void Approve(IDictionary<string, string> approvals, PinDecl pin) => approvals[pin.Id] = pin.ContentHash();

    public static void Revoke(IDictionary<string, string> approvals, string pinId) => approvals.Remove(pinId);

    public static bool IsApproved(IReadOnlyDictionary<string, string> approvals, PinDecl pin) =>
        approvals.TryGetValue(pin.Id, out var h) && string.Equals(h, pin.ContentHash(), StringComparison.Ordinal);

    /// 清掉已不存在、或內容已變更的核准（插件更新後呼叫）。回傳清掉幾筆。
    public static int PruneStale(IDictionary<string, string> approvals, ContribDecl current)
    {
        var stale = approvals.Keys.Where(id =>
        {
            var pin = current.Pins.FirstOrDefault(p => p.Id == id);
            return pin is null || approvals[id] != pin.ContentHash();
        }).ToList();
        foreach (var id in stale) approvals.Remove(id);
        return stale.Count;
    }
}

// ───────────────────────── 解析：哪些入口該顯示 ─────────────────────────

/// 主程式把每個已安裝插件的狀態整理成這個輸入。
public sealed record PluginContribInput(
    string PluginId,
    string PluginName,
    bool Enabled,
    IReadOnlyCollection<string> Granted,                  // 使用者已勾選的權限
    ContribDecl Contrib,
    IReadOnlyDictionary<string, string> Approvals);       // pinId → 核准時的內容雜湊

public sealed record ResolvedPin(string PluginId, string PluginName, string PinId, string PageId, string Title, string Icon, string Label);

public sealed record PendingPin(string PluginId, string PinId, string Title, string Reason);

public sealed class PinResolution
{
    public List<ResolvedPin> Visible { get; } = new();    // 該顯示在「更多」的入口
    public List<PendingPin> Pending { get; } = new();     // 有申請但沒顯示（附原因，給插件設定頁用）
}

public static class PinResolver
{
    public const int MaxVisible = 12;

    /// pageExists(pluginId, pageId)：該插件目前是否真的註冊了這個頁面（由執行環境回答）。
    public static PinResolution Resolve(IEnumerable<PluginContribInput> plugins, Func<string, string, bool> pageExists)
    {
        var result = new PinResolution();
        var candidates = new List<ResolvedPin>();

        foreach (var p in plugins.OrderBy(x => x.PluginName, StringComparer.CurrentCulture).ThenBy(x => x.PluginId, StringComparer.Ordinal))
        {
            // 插件名稱如果含奇怪字元，就改顯示 id，避免標籤被偽裝。
            var shownName = TextSafety.IsSafe(p.PluginName, allowEmojiJoiner: false) ? p.PluginName : p.PluginId;

            foreach (var pin in p.Contrib.Pins.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                string? why =
                    !p.Enabled ? "插件未啟用"
                    : !p.Granted.Contains("ui.page") ? "尚未授權 ui.page"
                    : !p.Approvals.ContainsKey(pin.Id) ? "尚未核准釘選"
                    : !PinPolicy.IsApproved(p.Approvals, pin) ? "內容已變更，需要重新核准"
                    : !pageExists(p.PluginId, pin.Page) ? "頁面尚未註冊"
                    : null;

                if (why is not null) { result.Pending.Add(new PendingPin(p.PluginId, pin.Id, pin.Title, why)); continue; }

                candidates.Add(new ResolvedPin(p.PluginId, shownName, pin.Id, pin.Page, pin.Title, pin.Icon,
                    Label: $"{pin.Title}（{shownName}）"));          // 一律附上來源
            }
        }

        foreach (var c in candidates)
        {
            if (result.Visible.Count < MaxVisible) result.Visible.Add(c);
            else result.Pending.Add(new PendingPin(c.PluginId, c.PinId, c.Title, $"超過「更多」頁上限（{MaxVisible} 個）"));
        }
        return result;
    }
}
