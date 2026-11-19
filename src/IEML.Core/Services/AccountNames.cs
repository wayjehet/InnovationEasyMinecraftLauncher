using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// 離線帳號名稱規則與重新命名（不動 IAccountService 契約；直接讀寫 accounts.json）。未編譯、未實測。
public static class AccountNames
{
    static readonly Regex Valid = new(@"^[A-Za-z0-9_]{3,16}$");

    /// 回傳 null 表示合法，否則是錯誤訊息。
    public static string? Validate(string name) =>
        Valid.IsMatch(name ?? "") ? null : "玩家名稱必須是 3～16 個英數字或底線";

    public static string OfflineUuid(string name)
    {
        var h = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        h[6] = (byte)((h[6] & 0x0f) | 0x30); h[8] = (byte)((h[8] & 0x3f) | 0x80);
        return new Guid(h).ToString("N");
    }

    /// 只改離線帳號；帳號 Id 不變，所以實例綁定與快速啟動卡片都保留。UUID 依新名稱重算（和遊戲離線模式一致）。
    public static async Task RenameOfflineAsync(Guid id, string newName)
    {
        var err = Validate(newName);
        if (err != null) throw new InvalidOperationException(err);
        var list = await JsonStore.ReadAsync(AppPaths.Accounts, () => new List<Account>());
        var acc = list.FirstOrDefault(a => a.Id == id) ?? throw new InvalidOperationException("帳號不存在");
        if (acc.Type != AccountType.Offline) throw new InvalidOperationException("正版帳號的名稱由 Microsoft 決定，不能改");
        if (list.Any(a => a.Id != id && a.Type == AccountType.Offline && a.Name == newName))
            throw new InvalidOperationException("已有同名的離線帳號");
        acc.Name = newName; acc.Uuid = OfflineUuid(newName);
        await JsonStore.WriteAsync(AppPaths.Accounts, list);
    }
}
