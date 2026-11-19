using System.Text.Json;
using System.Xml.Linq;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// 列出某個 Minecraft 版本可用的載入器版本（新→舊），給實例設置的下拉選單用。
public static class LoaderVersionService
{
    static readonly HttpClient Http = new();

    public static async Task<IReadOnlyList<string>> ListAsync(LoaderType loader, string mc, CancellationToken ct = default)
    {
        if (loader == LoaderType.Vanilla || string.IsNullOrWhiteSpace(mc)) return Array.Empty<string>();
        mc = mc.Trim();
        return loader switch
        {
            LoaderType.Fabric => await FromMeta("https://meta.fabricmc.net/v2/versions/loader", mc, ct),
            LoaderType.Quilt => await FromMeta("https://meta.quiltmc.org/v3/versions/loader", mc, ct),
            LoaderType.NeoForge => await NeoForge(mc, ct),
            LoaderType.Forge => await Forge(mc, ct),
            _ => Array.Empty<string>()
        };
    }

    // meta API 已經新→舊排序；穩定版放前面
    static async Task<IReadOnlyList<string>> FromMeta(string meta, string mc, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"{meta}/{Uri.EscapeDataString(mc)}", ct));
        var stable = new List<string>(); var other = new List<string>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var l = e.GetProperty("loader");
            var v = l.GetProperty("version").GetString()!;
            bool isStable = l.TryGetProperty("stable", out var st) ? st.GetBoolean() : !v.Contains('-');
            (isStable ? stable : other).Add(v);
        }
        return stable.Concat(other).ToList();
    }

    static async Task<IReadOnlyList<string>> NeoForge(string mc, CancellationToken ct)
    {
        var p = mc.Split('.');
        if (p.Length < 2 || p[0] != "1" || !int.TryParse(p[1], out var a)) return Array.Empty<string>();
        int b = p.Length > 2 && int.TryParse(p[2], out var bb) ? bb : 0;
        if (mc == "1.20.1")
        {
            var xl = XDocument.Parse(await Http.GetStringAsync("https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml", ct));
            return Sort(xl.Descendants("versions").Elements("version").Select(x => x.Value)
                .Where(v => v.StartsWith("1.20.1-")).Select(v => v["1.20.1-".Length..]));
        }
        if (a < 20 || (a == 20 && b < 2)) return Array.Empty<string>();
        var prefix = $"{a}.{b}.";
        var x = XDocument.Parse(await Http.GetStringAsync("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", ct));
        return Sort(x.Descendants("versions").Elements("version").Select(v => v.Value).Where(v => v.StartsWith(prefix)));
    }

    static async Task<IReadOnlyList<string>> Forge(string mc, CancellationToken ct)
    {
        var x = XDocument.Parse(await Http.GetStringAsync("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", ct));
        var pre = mc + "-";
        return Sort(x.Descendants("versions").Elements("version").Select(v => v.Value)
            .Where(v => v.StartsWith(pre)).Select(v => v[pre.Length..]));
    }

    // 穩定版（不含 '-'）在前；各自依版本號由新到舊
    static IReadOnlyList<string> Sort(IEnumerable<string> versions)
    {
        var all = versions.Distinct().ToList();
        all.Sort((x, y) =>
        {
            bool sx = !x.Contains('-'), sy = !y.Contains('-');
            if (sx != sy) return sx ? -1 : 1;
            return Compare(y, x);
        });
        return all;
    }

    static int Compare(string a, string b)
    {
        static int[] Parts(string s) => s.Split('.', '-').Select(t => int.TryParse(t, out var n) ? n : 0).ToArray();
        var (pa, pb) = (Parts(a), Parts(b));
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}
