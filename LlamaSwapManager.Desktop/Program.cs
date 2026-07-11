using System;
using System.Threading;
using Avalonia;

namespace LlamaSwapManager.Desktop;

sealed class Program
{
    private static readonly string MutexName = "Global\\LlamaSwapManager.SingleInstance";
    private static Mutex? _mutex;

    [STAThread]
    static void Main(string[] args)
    {
        CrashLogger.Install();

        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Console.WriteLine("LlamaDeck is already running.");
            return;
        }

        CrashLogger.Log("startup", $"LlamaDeck starting. LogPath={CrashLogger.LogPath}");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
         => AppBuilder.Configure<App>()
             .UsePlatformDetect()
             .LogToTrace();
}
