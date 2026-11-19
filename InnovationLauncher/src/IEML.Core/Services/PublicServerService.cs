using System.Text.Json;
namespace IEML.Core.Services;

/// 公開伺服器清單的一筆資料。
public record PublicServer(string Name, string Address, string? Version, string? Motd, int? Players, int? MaxPlayers, bool? Online);

/// 從 NewMinecraftServers 的公開 API（免金鑰、唯讀、JSON）搜尋公開伺服器。
/// 注意：
/// - 資料是站方排程檢查後的快取（30–60 秒），不是即時 ping；要即時狀態請再用 ServerPingService.PingAsync(Address)。
/// - 每頁最多 25 筆，頁碼 1–100；API 會拒絕未知或重複的查詢參數，所以只送 q / page。
/// - 回應欄位名稱「未實測」：這裡用寬鬆解析（遞迴找同時有名稱與位址的物件）。請先用實際回應確認。
public sealed class PublicServerService
{
    const string Base = "https://newminecraftservers.net/api/v1/servers";

    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    // 簡單快取：同一組查詢 60 秒內不重打 API（對方沒有公開配額，請客氣使用）
    readonly Dictionary<string, (DateTime At, IReadOnlyList<PublicServer> Data)> _cache = new();

    public async Task<IReadOnlyList<PublicServer>> SearchAsync(string keyword, int page = 1, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 100);
        var q = (keyword ?? "").Trim();
        if (q.Length > 160) q = q[..160];

        var key = $"{q}|{page}";
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromSeconds(60)) return hit.Data;

        var url = $"{Base}?page={page}" + (q.Length > 0 ? $"&q={Uri.EscapeDataString(q)}" : "");
        using var res = await Http.GetAsync(url, ct);
        if ((int)res.StatusCode == 429) throw new InvalidOperationException("公開伺服器 API 要求降低頻率（429），請稍後再試。");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = new List<PublicServer>();
        Walk(doc.RootElement, list);
        var data = list.DistinctBy(s => s.Address, StringComparer.OrdinalIgnoreCase).ToList();
        _cache[key] = (DateTime.UtcNow, data);
        return data;
    }

    static readonly string[] AddrKeys = { "address", "javaAddress", "javaIp", "ip", "host", "connection" };
    static readonly string[] NameKeys = { "name", "title" };

    static string? Str(JsonElement e, params string[] keys)
    {
        foreach (var k in keys)
            if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                return v.GetString();
        return null;
    }

    static int? Int(JsonElement e, params string[] keys)
    {
        foreach (var k in keys)
            if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        return null;
    }

    static void Walk(JsonElement e, List<PublicServer> output)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in e.EnumerateArray()) Walk(i, output);
            return;
        }
        if (e.ValueKind != JsonValueKind.Object) return;

        var name = Str(e, NameKeys);
        var addr = Str(e, AddrKeys);
        if (name is not null && addr is not null && addr.Contains('.'))
        {
            int? players = Int(e, "players", "playersOnline", "onlinePlayers", "online");
            int? max = Int(e, "maxPlayers", "playersMax", "max");
            // 有些 API 把人數放在 players: { online, max }
            if (e.TryGetProperty("players", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                players = Int(p, "online", "now", "count");
                max = Int(p, "max", "limit");
            }
            bool? online = null;
            foreach (var k in new[] { "online", "isOnline", "status" })
                if (e.TryGetProperty(k, out var o))
                {
                    if (o.ValueKind == JsonValueKind.True) { online = true; break; }
                    if (o.ValueKind == JsonValueKind.False) { online = false; break; }
                    if (o.ValueKind == JsonValueKind.String) { online = o.GetString()?.Equals("online", StringComparison.OrdinalIgnoreCase); break; }
                }
            output.Add(new PublicServer(name, addr.Trim(), Str(e, "version", "versions"), Str(e, "motd", "description", "shortDescription"), players, max, online));
            return;
        }
        foreach (var prop in e.EnumerateObject()) Walk(prop.Value, output);
    }
}
