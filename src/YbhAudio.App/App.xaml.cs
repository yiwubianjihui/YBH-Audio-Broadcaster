using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace YbhAudio.App;

public partial class App : Application
{
    private static readonly string TracePath =
        Path.Combine(AppContext.BaseDirectory, "startup.log");

    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(TracePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        }
        catch
        {
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Trace("OnStartup enter");
        base.OnStartup(e);
        Trace("base.OnStartup done");

        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrash(args.ExceptionObject as Exception);

        try
        {
            var window = new MainWindow();
            Trace("MainWindow constructed");
            MainWindow = window;
            window.Show();
            Trace("MainWindow shown, visible=" + window.IsVisible);
        }
        catch (Exception ex)
        {
            WriteCrash(ex);
            Trace("startup failed: " + ex.Message);
            MessageBox.Show(ex.ToString(), "YBH 音频广播助手 — 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Trace("OnExit code=" + e.ApplicationExitCode);
        base.OnExit(e);
    }

    private void OnDispatcherUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        Trace("dispatcher exception: " + e.Exception.Message);
        MessageBox.Show(e.Exception.Message, "YBH 音频广播助手 — 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteCrash(Exception? exception)
    {
        if (exception == null)
        {
            return;
        }
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}]\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // 写日志失败时不再抛异常
        }
    }
}
