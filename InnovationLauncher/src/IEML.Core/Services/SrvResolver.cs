using System.Runtime.InteropServices;
namespace IEML.Core.Services;

/// Minecraft 伺服器的 _minecraft._tcp.<host> SRV 記錄解析（Windows dnsapi.dll，免額外套件）。
/// 找不到記錄或非 Windows 時回傳 null，呼叫端照原本的 host:port 連線。未編譯、未實測。
public static class SrvResolver
{
    const ushort DNS_TYPE_SRV = 0x0021;
    const uint DNS_QUERY_STANDARD = 0;
    const int DnsFreeRecordList = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct DnsRecordHeader
    {
        public IntPtr pNext;
        public IntPtr pName;
        public ushort wType;
        public ushort wDataLength;
        public uint flags;
        public uint dwTtl;
        public uint dwReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DnsSrvData
    {
        public IntPtr pNameTarget;
        public ushort wPriority;
        public ushort wWeight;
        public ushort wPort;
        public ushort Pad;
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr result, IntPtr reserved);

    [DllImport("dnsapi.dll")]
    static extern void DnsRecordListFree(IntPtr list, int freeType);

    public static Task<(string Host, int Port)?> ResolveAsync(string host) => Task.Run(() => Resolve(host));

    public static (string Host, int Port)? Resolve(string host)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (System.Net.IPAddress.TryParse(host, out _)) return null; // IP 不查 SRV
        IntPtr list = IntPtr.Zero;
        try
        {
            if (DnsQuery($"_minecraft._tcp.{host}", DNS_TYPE_SRV, DNS_QUERY_STANDARD, IntPtr.Zero, out list, IntPtr.Zero) != 0
                || list == IntPtr.Zero) return null;

            (string Host, int Port, int Prio)? best = null;
            for (var p = list; p != IntPtr.Zero;)
            {
                var h = Marshal.PtrToStructure<DnsRecordHeader>(p);
                if (h.wType == DNS_TYPE_SRV)
                {
                    // 資料區緊接在標頭之後（x64 標頭 32 位元組；用 sizeof 計算）
                    var data = Marshal.PtrToStructure<DnsSrvData>(p + Marshal.SizeOf<DnsRecordHeader>());
                    var target = Marshal.PtrToStringUni(data.pNameTarget)?.TrimEnd('.');
                    if (!string.IsNullOrEmpty(target) && (best is null || data.wPriority < best.Value.Prio))
                        best = (target, data.wPort, data.wPriority);
                }
                p = h.pNext;
            }
            return best is null ? null : (best.Value.Host, best.Value.Port);
        }
        catch { return null; }
        finally { if (list != IntPtr.Zero) try { DnsRecordListFree(list, DnsFreeRecordList); } catch { } }
    }
}
