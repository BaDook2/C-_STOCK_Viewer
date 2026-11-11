using Avalonia;
using StockViewer.Core;

namespace StockViewer.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var log = DiagnosticLog.Start(version: typeof(Program).Assembly.GetName().Version?.ToString());
        void UnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            if (eventArgs.ExceptionObject is Exception e) DiagnosticLog.Write("session.unhandled", DiagnosticLog.Error(e), "error");
            log.Dispose();
        }
        AppDomain.CurrentDomain.UnhandledException += UnhandledException;
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception e) { DiagnosticLog.Write("session.failed", DiagnosticLog.Error(e), "error"); throw; }
        finally { AppDomain.CurrentDomain.UnhandledException -= UnhandledException; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
