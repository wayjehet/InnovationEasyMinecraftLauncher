using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

public class InstanceService : IInstanceService
{
    public string GetInstanceDir(Guid id) => Path.Combine(AppPaths.Instances, id.ToString("N"));
    string CfgPath(Guid id) => Path.Combine(GetInstanceDir(id), "instance.json");

    public async Task<IReadOnlyList<InstanceConfig>> ListAsync()
    {
        var res = new List<InstanceConfig>();
        foreach (var dir in Directory.GetDirectories(AppPaths.Instances))
        {
            var p = Path.Combine(dir, "instance.json");
            if (!File.Exists(p)) continue;
            res.Add(await JsonStore.ReadAsync(p, () => new InstanceConfig()));
        }
        return res.OrderBy(i => i.CreatedAt).ToList();
    }

    public async Task<InstanceConfig?> GetAsync(Guid id) =>
        File.Exists(CfgPath(id)) ? await JsonStore.ReadAsync(CfgPath(id), () => new InstanceConfig()) : null;

    public async Task<InstanceConfig> CreateAsync(InstanceConfig cfg)
    {
        var dir = GetInstanceDir(cfg.Id);
        foreach (var sub in new[] { "mods", "shaderpacks", "resourcepacks", "saves" })
            Directory.CreateDirectory(Path.Combine(dir, sub));
        await SaveAsync(cfg);
        return cfg;
    }

    public Task SaveAsync(InstanceConfig cfg) => JsonStore.WriteAsync(CfgPath(cfg.Id), cfg);

    public Task DeleteAsync(Guid id)
    {
        var d = GetInstanceDir(id);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        return Task.CompletedTask;
    }

    public async Task BindAccountAsync(Guid instanceId, Guid accountId)
    {
        var cfg = await GetAsync(instanceId) ?? throw new InvalidOperationException("Instance not found");
        cfg.AccountId = accountId;
        await SaveAsync(cfg);
    }
}
