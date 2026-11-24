using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using IEML.Core.Services;
namespace IEML.App;

public static class AppInfo
{
    public const string Repo = "https://github.com/wayjehet/InnovationEasyMinecraftLauncher";
    public static string Version
    {
        get
        {
            var v = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            var i = v.IndexOf('+');
            return i > 0 ? v[..i] : v;
        }
    }
}

/// 素材（電繪）載入：先找 %AppData%\InnovationEasyLauncher\Assets，再找 exe 旁邊的 Assets；找不到回傳 null（介面會退回預設樣式）。
public static class ArtAssets
{
    public static string UserDir => Path.Combine(AppPaths.Root, "Assets");

    public static string? Find(string name)
    {
        foreach (var dir in new[] { UserDir, Path.Combine(AppContext.BaseDirectory, "Assets") })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static BitmapImage? Load(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = Find(name);
        if (path is null) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;   // 讀完就放開檔案，不會鎖住
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}

/// XAML 用：{Binding Converter={StaticResource Asset}, ConverterParameter=background.png}
public class AssetConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ArtAssets.Load(parameter as string ?? value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// 附加屬性：local:ArtProps.File="btn_launch.png"（指定這個按鈕要用哪張圖）
public static class ArtProps
{
    public static readonly DependencyProperty FileProperty =
        DependencyProperty.RegisterAttached("File", typeof(string), typeof(ArtProps), new PropertyMetadata(null));
    public static string? GetFile(DependencyObject d) => (string?)d.GetValue(FileProperty);
    public static void SetFile(DependencyObject d, string? v) => d.SetValue(FileProperty, v);
}
