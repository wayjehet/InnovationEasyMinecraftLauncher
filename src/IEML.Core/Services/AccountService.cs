using System.Security.Cryptography;
using System.Text;
using CmlLib.Core.Auth;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

public class AccountService : IAccountService
{
    readonly SemaphoreSlim _lock = new(1, 1);
    async Task<List<Account>> Load() => await JsonStore.ReadAsync(AppPaths.Accounts, () => new List<Account>());

    public async Task<IReadOnlyList<Account>> ListAsync() => await Load();
    public async Task<Account?> GetAsync(Guid id) => (await Load()).FirstOrDefault(a => a.Id == id);

    public async Task<Account> AddOfflineAsync(string name)
    {
        await _lock.WaitAsync();
        try
        {
            var list = await Load();
            // Offline UUID = MD5("OfflinePlayer:" + name), same as vanilla
            var h = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
            h[6] = (byte)((h[6] & 0x0f) | 0x30); h[8] = (byte)((h[8] & 0x3f) | 0x80);
            var acc = new Account { Name = name, Type = AccountType.Offline, Uuid = new Guid(h).ToString("N") };
            list.Add(acc);
            await JsonStore.WriteAsync(AppPaths.Accounts, list);
            return acc;
        }
        finally { _lock.Release(); }
    }

    public async Task<Account> AddMicrosoftAsync()
    {
        var id = Guid.NewGuid();
        var handler = MicrosoftSessions.CreateHandler(id);
        var session = await handler.AuthenticateInteractively();   // opens the Microsoft sign-in window
        var uuid = session.UUID ?? "";

        await _lock.WaitAsync();
        try
        {
            var list = await Load();
            var existing = list.FirstOrDefault(a => a.Type == AccountType.Microsoft && a.Uuid == uuid);
            if (existing != null)   // same Minecraft profile already added -> keep the old entry
            {
                MicrosoftSessions.DeleteCache(id);
                existing.Name = session.Username ?? existing.Name;
                await JsonStore.WriteAsync(AppPaths.Accounts, list);
                return existing;
            }
            var acc = new Account { Id = id, Name = session.Username ?? "Player", Uuid = uuid, Type = AccountType.Microsoft };
            list.Add(acc);
            await JsonStore.WriteAsync(AppPaths.Accounts, list);
            return acc;
        }
        finally { _lock.Release(); }
    }

    public async Task<MSession> GetSessionAsync(Guid id)
    {
        var acc = await GetAsync(id) ?? throw new InvalidOperationException("帳號不存在");
        if (acc.Type == AccountType.Offline) return MSession.CreateOfflineSession(acc.Name);
        var handler = MicrosoftSessions.CreateHandler(id);
        try { return await handler.AuthenticateSilently(); }
        catch (Exception ex)
        {
            // 靜默登入失敗才會再跳登入視窗；把真正原因寫進遊戲日誌，方便查「為什麼又要重新登入」
            GameLog.Append($"Microsoft 靜默登入失敗（{acc.Name}）：{ex.GetType().Name}: {ex.Message}；快取檔：{MicrosoftSessions.CachePath(id)}"
                           + (File.Exists(MicrosoftSessions.CachePath(id)) ? "（存在）" : "（不存在）"));
            return await handler.AuthenticateInteractively();   // refresh token expired -> sign in again
        }
    }

    public async Task SaveRefreshTokenAsync(Guid id, string token)
    {
        await _lock.WaitAsync();
        try
        {
            var list = await Load();
            var acc = list.First(a => a.Id == id);
            acc.ProtectedRefreshToken = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
            await JsonStore.WriteAsync(AppPaths.Accounts, list);
        }
        finally { _lock.Release(); }
    }

    public async Task<string?> GetRefreshTokenAsync(Guid id)
    {
        var acc = await GetAsync(id);
        if (acc?.ProtectedRefreshToken is null) return null;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(acc.ProtectedRefreshToken), null, DataProtectionScope.CurrentUser));
    }

    public async Task RemoveAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            var list = await Load();
            list.RemoveAll(a => a.Id == id);
            MicrosoftSessions.DeleteCache(id);
            await JsonStore.WriteAsync(AppPaths.Accounts, list);
        }
        finally { _lock.Release(); }
    }
}
