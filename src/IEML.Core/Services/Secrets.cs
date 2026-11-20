using System.Security.Cryptography;
using System.Text;
namespace IEML.Core.Services;

/// DPAPI 加密工具：不同用途用不同的 optionalEntropy，一種用途的密文不能直接拿去當另一種用途解開。
/// 注意：entropy 寫在程式裡，不是祕密，擋不住同一 Windows 使用者下的惡意程式；它只做「用途隔離」。
public static class Secrets
{
    static byte[] Entropy(string purpose) => SHA256.HashData(Encoding.UTF8.GetBytes("IEML|" + purpose + "|v1"));
    public static readonly byte[] LoginCache = Entropy("login-cache");
    public static readonly byte[] ApiKey = Entropy("curseforge-api-key");
    public static readonly byte[] RefreshToken = Entropy("account-refresh-token");

    public static byte[] Protect(byte[] data, byte[] entropy) => ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
    public static byte[] Unprotect(byte[] data, byte[] entropy) => ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);

    /// 先用新 entropy 解；失敗就試舊版（entropy = null）。legacy = true 代表是舊格式，呼叫端應該重新加密存回去。
    public static byte[] UnprotectWithLegacy(byte[] data, byte[] entropy, out bool legacy)
    {
        try { legacy = false; return Unprotect(data, entropy); }
        catch (CryptographicException) { legacy = true; return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser); }
    }

    /// 用完就抹掉記憶體中的位元組。（.NET 的 string 無法抹除，所以憑證盡量只用 byte[] 處理。）
    public static void Zero(byte[]? data) { if (data is { Length: > 0 }) CryptographicOperations.ZeroMemory(data); }
}
