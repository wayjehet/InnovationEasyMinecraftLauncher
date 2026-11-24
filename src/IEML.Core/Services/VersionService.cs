using System.Text.RegularExpressions;
using CmlLib.Core;
namespace IEML.Core.Services;
/// 一個 Minecraft 版本（給「建立實例」的版本清單用）。Type：release／snapshot／old_beta／old_alpha。
public record GameVersionInfo(string Id, string Type, DateTime? Released)
{
    // 愚人節版本在官方清單裡都只是「snapshot」，沒有獨立類型，只能靠名單＋命名規則辨認：
    // 名單是已知的；規則是「第 13、14 週（4 月初）的快照，後綴不是單一字母」（例如 24w14potato、25w14craftmine）——
    // 往後新的愚人節版本通常也會符合；若漏掉或誤判，把版本號加進下面的名單即可。
    static readonly HashSet<string> AprilFoolsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "15w14a", "1.RV-Pre1", "3D Shareware v1.34", "20w14infinite", "22w13oneBlockAtATime",
        "23w13a_or_b", "24w14potato", "25w14craftmine"
    };
    static readonly Regex AprilFoolsPattern = new(@"^\d{2}w1[34][a-z]{2,}|_or_", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool IsAprilFools => Type != "release" && (AprilFoolsIds.Contains(Id) || AprilFoolsPattern.IsMatch(Id));
    public string TypeText => IsAprilFools ? "愚人節版本" : Type switch { "release" => "正式版", "snapshot" => "快照", "old_beta" => "舊版 Beta", "old_alpha" => "舊版 Alpha", _ => Type };
    /// release／snapshot／april（愚人節）／old（舊版 Beta 與 Alpha 歸在一起）。
    public string Group => IsAprilFools ? "april" : Type switch { "release" => "release", "snapshot" => "snapshot", _ => "old" };
    public string DateText => Released?.ToString("yyyy-MM-dd") ?? "";
}

public static class VersionService
{
    static readonly string[] Fallback = { "1.21.1", "1.20.4", "1.20.1", "1.19.4", "1.18.2", "1.16.5", "1.12.2", "1.8.9" };
    /// 完整的版本清單（含類型與發佈日期，新→舊）。離線時退回內建的幾個常用正式版（沒有日期）。
    public static async Task<IReadOnlyList<GameVersionInfo>> ListDetailedAsync()
    {
        try
        {
            var launcher = new MinecraftLauncher(new MinecraftPath(AppPaths.Shared));
            var all = await launcher.GetAllVersionsAsync();
            var list = all.Select(v => new GameVersionInfo(v.Name, v.Type ?? "release", ReleaseOf(v))).ToList();
            if (list.Count == 0) return FallbackInfos();
            // 有日期的依日期新→舊；沒日期的維持原順序放後面
            return list.Any(v => v.Released.HasValue)
                ? list.OrderByDescending(v => v.Released ?? DateTime.MinValue).ToList()
                : list;
        }
        catch { return FallbackInfos(); }
    }

    static IReadOnlyList<GameVersionInfo> FallbackInfos() => Fallback.Select(id => new GameVersionInfo(id, "release", null)).ToList();

    // CmlLib 的 ReleaseTime 型別（DateTime／DateTimeOffset／可為 null）因版本而異，用反射讀取以免綁死型別
    static DateTime? ReleaseOf(object v) => v.GetType().GetProperty("ReleaseTime")?.GetValue(v) switch
    {
        DateTime d => d,
        DateTimeOffset o => o.UtcDateTime,
        _ => null
    };

    public static async Task<IReadOnlyList<string>> ListAsync(bool includeSnapshots)
    {
        try
        {
            var launcher = new MinecraftLauncher(new MinecraftPath(AppPaths.Shared));
            var all = await launcher.GetAllVersionsAsync();
            var list = all.Where(v => includeSnapshots || v.Type == "release").Select(v => v.Name).ToList();
            return list.Count > 0 ? list : Fallback;
        }
        catch { return Fallback; } // offline etc.
    }
}
