using System.Runtime.InteropServices;
using System.Text;
using IEML.Core.Contracts;
namespace IEML.Core.Services;

/// 種子結構／群系查詢，底層是 Cubiomes（native/ieml_cubiomes_shim.c 編成 ieml_cubiomes.dll）。未編譯、未實測。
public class SeedMapService : ISeedMapService
{
    const string Dll = "ieml_cubiomes";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int ieml_find_structures([MarshalAs(UnmanagedType.LPStr)] string mc, [MarshalAs(UnmanagedType.LPStr)] string structure,
        long seed, int cx, int cz, int radius, int[] output, int maxOut);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int ieml_biome_at([MarshalAs(UnmanagedType.LPStr)] string mc, long seed, int x, int z, StringBuilder buf, int bufLen);

    /// (顯示名稱, Cubiomes 結構名稱)
    public static readonly (string Display, string Key)[] StructureTypes =
    {
        ("村莊", "Village"), ("沙漠神殿", "Desert_Pyramid"), ("叢林神廟", "Jungle_Temple"), ("沼澤小屋", "Swamp_Hut"),
        ("雪屋", "Igloo"), ("海底遺跡", "Ocean_Ruin"), ("沉船", "Shipwreck"), ("海底神殿", "Monument"),
        ("林地府邸", "Mansion"), ("掠奪者前哨站", "Outpost"), ("埋藏的寶藏", "Treasure"), ("傳送門遺跡", "Ruined_Portal"),
        ("古城", "Ancient_City"), ("考古遺跡", "Trail_Ruins"), ("試煉密室", "Trial_Chambers"),
        ("地獄要塞", "Fortress"), ("堡壘遺骸", "Bastion"), ("終界城", "End_City"),
    };

    public static bool IsAvailable()
    {
        try { ieml_biome_at("1.21", 0, 0, 0, new StringBuilder(64), 64); return true; }
        catch (DllNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return true; } // DLL 在，只是這次呼叫失敗
    }

    const string Missing = "找不到 ieml_cubiomes.dll。請依 native\\README.md 編譯後放到啟動器資料夾。";

    public IReadOnlyList<StructureHit> FindStructures(long seed, string mcVersion, string structureType, int centerX, int centerZ, int radius)
    {
        const int max = 2000;
        var buf = new int[max * 2];
        int n;
        try { n = ieml_find_structures(NormalizeMc(mcVersion), structureType, seed, centerX, centerZ, radius, buf, max); }
        catch (DllNotFoundException) { throw new InvalidOperationException(Missing); }
        if (n < 0) throw new InvalidOperationException(n switch
        {
            -1 => $"Cubiomes 不認得 Minecraft 版本「{mcVersion}」",
            -2 => $"不支援的結構類型：{structureType}",
            -3 => $"此版本（{mcVersion}）沒有「{structureType}」",
            _ => $"查詢失敗（錯誤碼 {n}）"
        });
        var list = new List<StructureHit>(n);
        for (int i = 0; i < n; i++) list.Add(new StructureHit(structureType, buf[i * 2], buf[i * 2 + 1]));
        return list.OrderBy(h => (long)(h.X - centerX) * (h.X - centerX) + (long)(h.Z - centerZ) * (h.Z - centerZ)).ToList();
    }

    public string GetBiome(long seed, string mcVersion, int x, int z)
    {
        var sb = new StringBuilder(64);
        int r;
        try { r = ieml_biome_at(NormalizeMc(mcVersion), seed, x, z, sb, sb.Capacity); }
        catch (DllNotFoundException) { throw new InvalidOperationException(Missing); }
        if (r != 0) throw new InvalidOperationException($"查詢群系失敗（錯誤碼 {r}）");
        return sb.ToString();
    }

    /// "1.21.1" -> 保留；"1.20.4-pre1" 之類取數字部分。Cubiomes 只認 "1.21" 這種主版本字串的話會往下對齊。
    static string NormalizeMc(string v)
    {
        var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"^\d+\.\d+(\.\d+)?");
        return m.Success ? m.Value : v ?? "";
    }

    /// 與遊戲相同的種子規則：純數字就當 long；空字串＝隨機（呼叫端處理）；其他文字用 Java String.hashCode()。
    public static long ParseSeed(string text)
    {
        text = (text ?? "").Trim();
        if (long.TryParse(text, out var n)) return n;
        int h = 0;
        foreach (var ch in text) h = unchecked(31 * h + ch);
        return h;
    }
}
