using CmlLib.Core.Auth.Microsoft;
namespace IEML.Core.Services;

/// One CmlLib login handler (+ its own token cache file) per launcher account,
/// so several Microsoft accounts can coexist and each instance/card picks its own.
/// NOTE: written against CmlLib.Core.Auth.Microsoft 3.x from memory; fix names if the build complains.
public static class MicrosoftSessions
{
    static string Dir => Path.Combine(AppPaths.Root, "ms");
    public static string CachePath(Guid id) => Path.Combine(Dir, $"{id:N}.json");

    public static JELoginHandler CreateHandler(Guid id)
    {
        Directory.CreateDirectory(Dir);
        return new JELoginHandlerBuilder().WithAccountManager(CachePath(id)).Build();
    }

    public static void DeleteCache(Guid id)
    {
        try { var p = CachePath(id); if (File.Exists(p)) File.Delete(p); } catch { }
    }
}
