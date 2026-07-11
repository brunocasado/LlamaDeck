using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LlamaSwapManager.Desktop;

internal static class CrashLogger
{
    private static readonly object Lock = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LlamaSwapManager",
        "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain.CurrentDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log("Dispatcher.UIThread.UnhandledException", e.Exception);
            e.Handled = true;
            TryShowError(e.Exception);
        };
    }

    public static void Log(string source, Exception? exception)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"\n==================== {DateTimeOffset.Now:O} ====================\n" +
                    $"Source: {source}\n" +
                    $"Exception: {exception}\n" +
                    $"==============================================================\n");
            }
        }
        catch
        {
            // Never let crash logging crash the app.
        }
    }

    public static void Log(string source, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"\n==================== {DateTimeOffset.Now:O} ====================\n" +
                    $"Source: {source}\n" +
                    $"Message: {message}\n" +
                    $"==============================================================\n");
            }
        }
        catch
        {
        }
    }

    private static void TryShowError(Exception exception)
    {
        try
        {
            var window = new Window
            {
                Title = "LlamaDeck error",
                Width = 760,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new TextBox
                {
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = $"An error was captured and the app tried to continue.\n\nLog file:\n{LogPath}\n\n{exception}"
                }
            };

            window.Show();
        }
        catch
        {
        }
    }
}
