using CmlLib.Core;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// 建立實例後「預先安裝」：下載原版遊戲檔案、安裝載入器、補齊載入器所需檔案，讓第一次啟動不用再等。
/// 邏輯和 LaunchService 的安裝部分相同，但不需要帳號、也不會啟動遊戲。
public class GameInstaller
{
    readonly IInstanceService _instances;
    readonly JavaService _java = new();
    public GameInstaller(IInstanceService instances) => _instances = instances;

    public async Task PrepareAsync(Guid instanceId, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default)
    {
        var cfg = await _instances.GetAsync(instanceId) ?? throw new InvalidOperationException("找不到實例");

        progress?.Report(("檢查 Java", 0));
        var java = await _java.ResolveAsync(cfg, progress, ct);

        var path = new MinecraftPath(_instances.GetInstanceDir(cfg.Id));
        SharedCache.Apply(path, cfg.Loader); // 共用 libraries／assets／runtime（Forge／NeoForge 除外），與啟動時一致
        var launcher = new MinecraftLauncher(path);
        launcher.FileProgressChanged += (_, e) =>
            progress?.Report(($"下載 {e.Name}", e.TotalTasks == 0 ? 0 : 100.0 * e.ProgressedTasks / e.TotalTasks));

        progress?.Report(("下載遊戲檔案", 0));
        await launcher.InstallAsync(cfg.McVersion, ct); // Forge／NeoForge 的安裝程式需要先有原版

        if (cfg.Loader != LoaderType.Vanilla)
        {
            progress?.Report(($"安裝 {cfg.Loader}", 0));
            var before = cfg.LoaderVersion;
            var savedJava = cfg.JavaPath;
            cfg.JavaPath = java.Path; // 載入器安裝程式也要用選好的 Java；完成後還原，避免把自動選的路徑存進實例設定
            string versionId;
            try { versionId = await LoaderService.EnsureInstalledAsync(cfg, _instances.GetInstanceDir(cfg.Id), progress, ct); }
            finally { cfg.JavaPath = savedJava; }
            if (before != cfg.LoaderVersion) await _instances.SaveAsync(cfg);

            progress?.Report(($"下載 {cfg.Loader} 所需檔案", 0));
            await launcher.InstallAsync(versionId, ct);
        }
        progress?.Report(("完成", 100));
    }
}
