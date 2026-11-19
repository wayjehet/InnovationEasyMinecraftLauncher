using CmlLib.Core;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// 共用資源快取：多個實例共用 libraries / assets / runtime，省硬碟與下載時間。
/// 放在 %AppData%\InnovationEasyLauncher\shared\。
/// 注意：
/// - versions 不共用：LoaderService 會把 Fabric/Quilt 的 profile JSON 與 Forge/NeoForge 安裝結果寫進「實例自己的」versions，共用會互相干擾。
/// - Forge / NeoForge 不共用：官方安裝程式會把函式庫直接裝進實例資料夾（--installClient），若啟動時指向 shared 會找不到。
/// - 未驗證：MinecraftPath 的 Library / Assets / Resource / Runtime 屬性名稱與是否可寫，是憑 CmlLib 4.x 記憶寫的，編譯失敗請以實際 API 為準。
public static class SharedCache
{
    public static string Libraries => Path.Combine(AppPaths.Shared, "libraries");
    public static string Assets => Path.Combine(AppPaths.Shared, "assets");
    public static string Resources => Path.Combine(AppPaths.Shared, "resources");
    public static string Runtime => Path.Combine(AppPaths.Shared, "runtime");

    public static bool Supports(LoaderType loader) => loader is not (LoaderType.Forge or LoaderType.NeoForge);

    public static void Apply(MinecraftPath path, LoaderType loader)
    {
        if (!Supports(loader)) return;
        foreach (var d in new[] { Libraries, Assets, Resources, Runtime }) Directory.CreateDirectory(d);
        path.Library = Libraries;
        path.Assets = Assets;
        path.Resource = Resources;
        path.Runtime = Runtime;
    }
}
