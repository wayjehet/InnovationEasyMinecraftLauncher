using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// Vanilla + offline accounts for now. Fabric/Forge/Microsoft are TODO.
/// NOTE: written against CmlLib.Core 4.x API from memory, not compiled yet; adjust if names differ.
public class LaunchService : ILaunchService
{
    readonly IInstanceService _instances;
    readonly IAccountService _accounts;
    readonly JavaService _java = new();
    public LaunchService(IInstanceService instances, IAccountService accounts)
    { _instances = instances; _accounts = accounts; }

    /// 包一層：啟動前的任何錯誤（驗證、下載、載入器安裝）也寫進遊戲日誌，再照常往上丟給 UI 顯示在 Status。
    static string? JoinHost(string? a) => string.IsNullOrWhiteSpace(a) ? null : a.Trim().Split(':')[0];
    static int JoinPort(string? a)
    {
        var parts = a?.Trim().Split(':');
        return parts is { Length: > 1 } && int.TryParse(parts[1], out var p) ? p : 25565;
    }

    public async Task<int> LaunchAsync(Guid instanceId, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default, Guid? accountIdOverride = null, string? serverOverride = null)
    {
        try { return await LaunchCoreAsync(instanceId, progress, ct, accountIdOverride, serverOverride); }
        catch (OperationCanceledException) { GameLog.Append("=== 啟動已取消 ==="); throw; }
        catch (Exception ex) { GameLog.Append("=== 啟動失敗：" + ex.Message + " ==="); throw; }
    }

    async Task<int> LaunchCoreAsync(Guid instanceId, IProgress<(string Stage, double Percent)>? progress, CancellationToken ct, Guid? accountIdOverride, string? serverOverride)
    {
        var cfg = await _instances.GetAsync(instanceId) ?? throw new InvalidOperationException("找不到實例");
        var accId = accountIdOverride ?? cfg.AccountId;
        if (accId is null) throw new InvalidOperationException("此實例尚未選擇帳號");
        progress?.Report(("驗證帳號", 0));
        MSession session = await _accounts.GetSessionAsync(accId.Value);

        progress?.Report(("檢查 Java", 0));
        var java = await _java.ResolveAsync(cfg, progress, ct);
        GameLog.Append($"Java {java.Major}（{java.SourceText}）：{java.Path}");

        var path = new MinecraftPath(_instances.GetInstanceDir(cfg.Id));
        SharedCache.Apply(path, cfg.Loader); // 共用 libraries/assets/runtime（Forge/NeoForge 除外）
        var launcher = new MinecraftLauncher(path);
        launcher.FileProgressChanged += (_, e) =>
            progress?.Report(($"下載 {e.Name}", e.TotalTasks == 0 ? 0 : 100.0 * e.ProgressedTasks / e.TotalTasks));

        var opt = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = cfg.MaxMemoryMb,
            JavaPath = java.Path,
            ServerIp = JoinHost(serverOverride ?? cfg.AutoJoinServer),     // 這次指定的伺服器優先於「自動加入」
            ServerPort = JoinPort(serverOverride ?? cfg.AutoJoinServer),
            ExtraJvmArguments = (cfg.ExtraJvmArgs ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => new MArgument(a)).ToArray()
        };

        progress?.Report(("準備啟動", 0));
        var versionId = cfg.McVersion;
        if (cfg.Loader != LoaderType.Vanilla)
        {
            if (cfg.Loader is LoaderType.Forge or LoaderType.NeoForge)
            {
                // installers need vanilla (and the bundled Java runtime) present first
                progress?.Report(("安裝原版", 0));
                await launcher.InstallAsync(cfg.McVersion, ct);
            }
            progress?.Report(($"安裝 {cfg.Loader}", 0));
            var before = cfg.LoaderVersion;
            // 載入器安裝程式也要用選好的 Java；暫時覆蓋 JavaPath，完成後還原，避免把自動選的路徑存進實例設定
            var savedJava = cfg.JavaPath;
            cfg.JavaPath = java.Path;
            try { versionId = await LoaderService.EnsureInstalledAsync(cfg, _instances.GetInstanceDir(cfg.Id), progress, ct); }
            finally { cfg.JavaPath = savedJava; }
            if (before != cfg.LoaderVersion) await _instances.SaveAsync(cfg);
        }
        var process = await launcher.InstallAndBuildProcessAsync(versionId, opt, ct);
        GameLog.Start(process, cfg.Name, session.AccessToken); // 轉向輸出並啟動（取代 process.Start()）
        progress?.Report(("已啟動", 100));
        return process.Id;
    }
}
