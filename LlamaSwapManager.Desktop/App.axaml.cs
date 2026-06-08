using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using LlamaSwapManager.Services;
using LlamaSwapManager.ViewModels;
using LlamaSwapManager.Views;

namespace LlamaSwapManager;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _startMenuItem;
    private NativeMenuItem? _stopMenuItem;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var mainWindow = new MainWindow { DataContext = vm };
            desktop.MainWindow = mainWindow;

            if (OperatingSystem.IsMacOS())
            {
                try
                {
                    SetMacOsDockIcon();
                }
                catch { /* fallback */ }
            }

            SetupTrayIcon(mainWindow, vm);

            // Keep menu items synced with process state
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Status))
                    UpdateTrayMenuState(vm);
            };
            UpdateTrayMenuState(vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(Window mainWindow, MainViewModel vm)
    {
        try
        {
            var uri = new Uri("avares://LlamaSwapManager.Desktop/Assets/llama.png");
            using var stream = AssetLoader.Open(uri);
            var icon = new WindowIcon(stream);

            // ---- Tray Menu Items ----
            var showItem = new NativeMenuItem("Show")
            {
                Command = new AsyncRelayCommand(() =>
                {
                    ShowWindow(mainWindow);
                    return System.Threading.Tasks.Task.CompletedTask;
                })
            };

            _startMenuItem = new NativeMenuItem("Start")
            {
                Command = new AsyncRelayCommand(async () =>
                {
                    if (vm.StartCommand is AsyncRelayCommand cmd)
                        await cmd.ExecuteAsync(null);
                    ShowWindow(mainWindow);
                })
            };

            _stopMenuItem = new NativeMenuItem("Stop")
            {
                Command = new AsyncRelayCommand(async () =>
                {
                    if (vm.StopCommand is AsyncRelayCommand cmd)
                        await cmd.ExecuteAsync(null);
                })
            };

            var restartItem = new NativeMenuItem("Restart")
            {
                Command = new AsyncRelayCommand(async () =>
                {
                    if (vm.RestartCommand is AsyncRelayCommand cmd)
                        await cmd.ExecuteAsync(null);
                    ShowWindow(mainWindow);
                })
            };

            var quitItem = new NativeMenuItem("Quit")
            {
                Command = new AsyncRelayCommand(async () =>
                {
                    await vm.QuitApplicationAsync();
                })
            };

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "LlamaSwapManager",
                Menu = new NativeMenu
                {
                    Items =
                    {
                        showItem,
                        new NativeMenuItemSeparator(),
                        _startMenuItem,
                        _stopMenuItem,
                        restartItem,
                        new NativeMenuItemSeparator(),
                        quitItem
                    }
                }
            };

            // Double-click / click on tray icon opens the window
            _trayIcon.Clicked += (_, _) =>
            {
                ShowWindow(mainWindow);
            };
        }
        catch
        {
            // Fallback se o ícone não carregar
        }
    }

    private static void ShowWindow(Window mainWindow)
    {
        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Topmost = true;
        mainWindow.Activate();
        mainWindow.Topmost = false;
    }

    private void UpdateTrayMenuState(MainViewModel vm)
    {
        if (_startMenuItem == null || _stopMenuItem == null) return;

        var isRunning = vm.Status == LlamaSwapStatus.Running || vm.Status == LlamaSwapStatus.Starting;
        var isStopped = vm.Status == LlamaSwapStatus.Stopped || vm.Status == LlamaSwapStatus.Error;

        _startMenuItem.IsEnabled = isStopped;
        _stopMenuItem.IsEnabled = isRunning;
    }

    // ── macOS Dock Icon (nativa) ───────────────────────────────────
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend_str(IntPtr receiver, IntPtr selector, string arg);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    private static void SetMacOsDockIcon()
    {
        // O PNG está empacotado como recurso; carrega via AssetLoader primeiro,
        // e como fallback tenta o caminho do filesystem.
        byte[]? pngBytes = null;
        try
        {
            var uri = new Uri("avares://LlamaSwapManager.Desktop/Assets/llama.png");
            using var stream = AssetLoader.Open(uri);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            pngBytes = ms.ToArray();
        }
        catch { /* fallback */ }

        if (pngBytes == null || pngBytes.Length == 0)
        {
            // Tenta o filesystem (via dotnet run ou build)
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Assets", "llama.png"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "llama.png")),
            };
            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    pngBytes = File.ReadAllBytes(p);
                    break;
                }
            }
        }

        if (pngBytes == null || pngBytes.Length == 0) return;

        // NSData *data = [NSData dataWithBytes:pngBytes length:len];
        // Pin the managed array so the GC doesn't move it during the native call.
        var gcHandle = GCHandle.Alloc(pngBytes, GCHandleType.Pinned);
        try
        {
            var ptr = gcHandle.AddrOfPinnedObject();
            var nsDataClass = objc_getClass("NSData");
            var selDataWithBytes = sel_registerName("dataWithBytes:length:");
            var data = objc_msgSend_IntPtr_Int(nsDataClass, selDataWithBytes, ptr, pngBytes.Length);

            // NSImage *img = [[NSImage alloc] initWithData:data];
            var nsImageClass = objc_getClass("NSImage");
            var selAlloc = sel_registerName("alloc");
            var img = objc_msgSend(nsImageClass, selAlloc);
            var selInitWithData = sel_registerName("initWithData:");
            img = objc_msgSend_IntPtr(img, selInitWithData, data);

            if (img == IntPtr.Zero) return;

            // NSApplication *app = [NSApplication sharedApplication];
            var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));

            // [app setApplicationIconImage:img];
            objc_msgSend_IntPtr(app, sel_registerName("setApplicationIconImage:"), img);
        }
        finally
        {
            gcHandle.Free();
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend_IntPtr_Int(IntPtr receiver, IntPtr selector, IntPtr bytes, int len);
}
