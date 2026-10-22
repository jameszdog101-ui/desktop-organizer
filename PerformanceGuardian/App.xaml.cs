using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PerformanceGuardian;

public partial class App : System.Windows.Application
{
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartDesktopGuardian", "startup-errors.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteStartupError("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteStartupError("TaskScheduler", args.Exception);
            args.SetObserved();
        };

        try
        {
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            WriteStartupError("Startup", ex);
            ShowStartupFailure(ex);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteStartupError("Dispatcher", e.Exception);
        System.Windows.MessageBox.Show($"程式發生錯誤，診斷紀錄已儲存於：\n{StartupLogPath}\n\n{e.Exception.Message}",
            "Smart Desktop Guardian", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void ShowStartupFailure(Exception exception) =>
        System.Windows.MessageBox.Show($"Smart Desktop Guardian 無法啟動。\n\n{exception.Message}\n\n診斷紀錄：\n{StartupLogPath}",
            "啟動失敗", MessageBoxButton.OK, MessageBoxImage.Error);

    private static void WriteStartupError(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
            var text = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:O}] {source}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"Runtime: {Environment.Version}")
                .AppendLine($"Process: {Environment.ProcessPath}")
                .AppendLine(exception?.ToString() ?? "Unknown startup failure")
                .AppendLine(new string('-', 72))
                .ToString();
            File.AppendAllText(StartupLogPath, text, Encoding.UTF8);
        }
        catch { }
    }
}
