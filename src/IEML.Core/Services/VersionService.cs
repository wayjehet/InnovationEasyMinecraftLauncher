using CmlLib.Core;
namespace IEML.Core.Services;
public static class VersionService
{
    static readonly string[] Fallback = { "1.21.1", "1.20.4", "1.20.1", "1.19.4", "1.18.2", "1.16.5", "1.12.2", "1.8.9" };
    public static async Task<IReadOnlyList<string>> ListAsync(bool includeSnapshots)
    {
        try
        {
            var launcher = new MinecraftLauncher(new MinecraftPath(AppPaths.Shared));
            var all = await launcher.GetAllVersionsAsync();
            var list = all.Where(v => includeSnapshots || v.Type == "release").Select(v => v.Name).ToList();
            return list.Count > 0 ? list : Fallback;
        }
        catch { return Fallback; } // offline etc.
    }
}
