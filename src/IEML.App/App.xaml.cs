using System.IO;
using System.Windows;
using System.Windows.Threading;
using IEML.Core.Services;
namespace IEML.App;

public partial class App : System.Windows.Application
{
    public App()
    {
        // 介面執行緒上沒人接的例外：記錄下來並提示，而不是整個程式無聲消失
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // 其他執行緒的例外：只能記錄（.NET 會結束程式，但至少留下原因）
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { WriteCrashLog("Task", e.Exception); e.SetObserved(); };
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("UI", e.Exception);
        e.Handled = true;   // 繼續執行，不讓一個按鈕的錯誤把整個啟動器關掉
        try
        {
            MessageBox.Show(
                "發生未預期的錯誤，但啟動器會繼續執行。\n\n" + e.Exception.Message +
                "\n\n詳細記錄已寫入：\n" + CrashLogPath +
                "\n\n回報問題時請附上這個檔案。",
                "Innovation Easy Minecraft Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
    }

    static string CrashLogPath => Path.Combine(AppPaths.Root, "crash.log");

    static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) {ex}\r\n\r\n");
        }
        catch { }
    }
}
