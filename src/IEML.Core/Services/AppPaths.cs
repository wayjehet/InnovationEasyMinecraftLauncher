namespace IEML.Core.Services;
public static class AppPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InnovationEasyLauncher");
    public static string Accounts => Path.Combine(Root, "accounts.json");
    public static string Instances => Path.Combine(Root, "instances");
    public static string Shared => Path.Combine(Root, "shared");     // assets/libraries/versions cache
    public static string Tools => Path.Combine(Root, "tools");       // easytier-core.exe etc.
    public static string Music => Path.Combine(Root, "music");
    static AppPaths() { foreach (var d in new[] { Root, Instances, Shared, Tools, Music }) Directory.CreateDirectory(d); }
}
