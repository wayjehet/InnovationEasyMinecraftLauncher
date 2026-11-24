using System.Text.Json;
namespace IEML.Core.Services;

/// 公開伺服器清單的一筆資料。
public record PublicServer(string Name, string Address, string? Version, string? Motd, int? Players, int? MaxPlayers, bool? Online);

/// 從 NewMinecraftServers 的公開 API（免金鑰、唯讀、JSON）搜尋公開伺服器。
/// 注意：
/// - 資料是站方排程檢查後的快取（30–60 秒），不是即時 ping；要即時狀態請再用 ServerPingService.PingAsync(Address)。
/// - 每頁最多 25 筆，頁碼 1–100；API 會拒絕未知或重複的查詢參數，所以只送 q / page。
/// - 回應格式依官方 OpenAPI：{ data: { items: [ServerSummary], page, totalPages, total } }；位址在 displayAddress／primaryConnection，人數與版本在 primarySnapshot。
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
    readonly Dictionary<string, (DateTime At, IReadOnlyList<PublicServer> Data, int TotalPages, int Total)> _cache = new();

    /// 最近一次查詢回報的總頁數（API 的 data.totalPages；沒有就是 0）。
    public int LastTotalPages { get; private set; }
    public int LastTotal { get; private set; }

    public async Task<IReadOnlyList<PublicServer>> SearchAsync(string keyword, int page = 1, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 100);
        var q = (keyword ?? "").Trim();
        if (q.Length > 160) q = q[..160];

        var key = $"{q}|{page}";
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromSeconds(60))
        { LastTotalPages = hit.TotalPages; LastTotal = hit.Total; return hit.Data; }

        // edition=java：本啟動器只能啟動 Java 版；limit=25 是上限。API 會拒絕未知或重複的參數，只送文件列出的。
        var url = $"{Base}?edition=java&limit=25&page={page}" + (q.Length > 0 ? $"&q={Uri.EscapeDataString(q)}" : "");
        using var res = await Http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if ((int)res.StatusCode == 429) throw new InvalidOperationException("公開伺服器 API 要求降低頻率（429），請稍後再試。");
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"公開伺服器 API 回應 {(int)res.StatusCode}：{ErrorMessage(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var data = root.TryGetProperty("data", out var d) ? d : root;

        var list = new List<PublicServer>();
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var it in items.EnumerateArray()) { var parsed = ParseSummary(it); if (parsed is not null) list.Add(parsed); }
        else
            Walk(data, list); // 格式與文件不同時的備援

        var result = list.DistinctBy(s => s.Address, StringComparer.OrdinalIgnoreCase).ToList();
        int totalPages = data.ValueKind == JsonValueKind.Object ? Int(data, "totalPages") ?? 0 : 0;
        int total = data.ValueKind == JsonValueKind.Object ? Int(data, "total") ?? 0 : 0;
        LastTotalPages = totalPages; LastTotal = total;
        _cache[key] = (DateTime.UtcNow, result, totalPages, total);
        return result;
    }

    static string ErrorMessage(string body)
    {
        try
        {
            using var d = JsonDocument.Parse(body);
            if (d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
                return (Str(e, "message") ?? Str(e, "code")) ?? body;
        }
        catch { }
        return body.Length > 200 ? body[..200] : body;
    }

    /// 依官方 OpenAPI：ServerSummary { name, shortDescription, displayAddress{host,port}, primaryConnection{edition,host,port},
    /// primarySnapshot{outcome,playersOnline,playersMax,versionName}, media{motdLines} }
    static PublicServer? ParseSummary(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var name = Str(e, "name", "title");
        if (name is null) return null;

        string? host = null; int? port = null;
        if (e.TryGetProperty("displayAddress", out var da) && da.ValueKind == JsonValueKind.Object)
        { host = Str(da, "host"); port = Int(da, "port"); }
        if (host is null && e.TryGetProperty("primaryConnection", out var pc) && pc.ValueKind == JsonValueKind.Object)
        { host = Str(pc, "host", "address"); port = Int(pc, "port"); }
        host ??= Str(e, "address", "ip", "host");
        if (host is null) return null;
        var addr = host.Trim();
        if (port is int pt && pt != 25565 && !addr.Contains(':') ) addr += ":" + pt;
        else if (port is int pt2 && pt2 != 25565 && addr.StartsWith('[') && !addr.Contains("]:")) addr += ":" + pt2;

        string? version = null, motd = Str(e, "shortDescription");
        int? players = null, max = null; bool? online = null;
        if (e.TryGetProperty("primarySnapshot", out var sn) && sn.ValueKind == JsonValueKind.Object)
        {
            version = Str(sn, "versionName");
            players = Int(sn, "playersOnline"); max = Int(sn, "playersMax");
            var outcome = Str(sn, "outcome");
            if (outcome is not null) online = outcome.Equals("online", StringComparison.OrdinalIgnoreCase) ? true
                                            : outcome.Equals("offline", StringComparison.OrdinalIgnoreCase) ? false : null;
        }
        if (motd is null && e.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object
            && media.TryGetProperty("motdLines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var line in lines.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.Array) continue;
                foreach (var seg in line.EnumerateArray()) sb.Append(Str(seg, "text"));
                sb.Append(' ');
            }
            motd = sb.ToString().Trim();
            if (motd.Length == 0) motd = null;
        }
        return new PublicServer(name, addr, version, motd, players, max, online);
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
