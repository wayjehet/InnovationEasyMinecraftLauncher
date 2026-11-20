using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using IEML.Core.Contracts;
namespace IEML.Core.Services;

/// Minecraft Server List Ping (TCP). SRV records are not resolved yet (TODO).
public class ServerPingService : IServerPingService
{
    static string FavPath => Path.Combine(AppPaths.Root, "servers.json");
    public async Task<List<string>> LoadFavoritesAsync() => await JsonStore.ReadAsync(FavPath, () => new List<string>());
    public async Task SaveFavoriteAsync(string addr)
    {
        var l = await LoadFavoritesAsync();
        if (!l.Contains(addr)) { l.Add(addr); await JsonStore.WriteAsync(FavPath, l); }
    }

    public async Task<IReadOnlyList<ServerStatus>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        var favs = await LoadFavoritesAsync();
        var res = await Task.WhenAll(favs.Select(a => PingAsync(a, ct)));
        return res.Where(r => string.IsNullOrWhiteSpace(keyword) ||
            r.Address.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            r.Motd.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<ServerStatus> PingAsync(string address, CancellationToken ct = default)
    {
        var (host, port) = Parse(address);
        // 沒指定埠（或預設 25565）時先查 SRV，找到就改連真正的主機與埠；握手封包仍用原本輸入的主機名
        var hsHost = host;
        if (!address.Contains(':'))
        {
            var srv = await SrvResolver.ResolveAsync(host);
            if (srv is not null) (host, port) = (srv.Value.Host, srv.Value.Port);
        }
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            var ns = client.GetStream();

            var hs = new MemoryStream();
            WriteVarInt(hs, 0); WriteVarInt(hs, -1);
            WriteString(hs, hsHost);
            hs.WriteByte((byte)(port >> 8)); hs.WriteByte((byte)(port & 0xFF));
            WriteVarInt(hs, 1);
            await SendPacket(ns, hs.ToArray(), cts.Token);
            await SendPacket(ns, new byte[] { 0x00 }, cts.Token);

            await ReadVarInt(ns, cts.Token);               // packet length
            await ReadVarInt(ns, cts.Token);               // packet id
            int len = await ReadVarInt(ns, cts.Token);
            var buf = new byte[len];
            await ns.ReadExactlyAsync(buf, cts.Token);
            sw.Stop();

            using var doc = JsonDocument.Parse(buf);
            var r = doc.RootElement;
            var players = r.GetProperty("players");
            return new ServerStatus(address, true,
                r.TryGetProperty("description", out var d) ? Flatten(d) : "",
                players.GetProperty("online").GetInt32(), players.GetProperty("max").GetInt32(),
                r.TryGetProperty("version", out var v) ? v.GetProperty("name").GetString() ?? "" : "",
                (int)sw.ElapsedMilliseconds);
        }
        catch { return new ServerStatus(address, false, "", 0, 0, "", -1); }
    }

    static (string, int) Parse(string a)
    {
        var parts = a.Trim().Split(':');
        return (parts[0], parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 25565);
    }

    static string Flatten(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
        var sb = new StringBuilder();
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("text", out var t)) sb.Append(t.GetString());
            if (e.TryGetProperty("extra", out var ex)) foreach (var c in ex.EnumerateArray()) sb.Append(Flatten(c));
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var c in e.EnumerateArray()) sb.Append(Flatten(c));
        return sb.ToString();
    }

    static void WriteVarInt(Stream s, int value)
    {
        uint v = (uint)value;
        while (true)
        {
            if ((v & ~0x7Fu) == 0) { s.WriteByte((byte)v); return; }
            s.WriteByte((byte)((v & 0x7F) | 0x80)); v >>= 7;
        }
    }
    static void WriteString(Stream s, string str)
    {
        var b = Encoding.UTF8.GetBytes(str); WriteVarInt(s, b.Length); s.Write(b);
    }
    static async Task SendPacket(Stream s, byte[] data, CancellationToken ct)
    {
        var ms = new MemoryStream(); WriteVarInt(ms, data.Length); ms.Write(data);
        await s.WriteAsync(ms.ToArray(), ct);
    }
    static async Task<int> ReadVarInt(Stream s, CancellationToken ct)
    {
        int result = 0, shift = 0; var one = new byte[1];
        while (true)
        {
            await s.ReadExactlyAsync(one, ct);
            result |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0) return result;
            shift += 7; if (shift > 35) throw new InvalidDataException("VarInt too big");
        }
    }
}
