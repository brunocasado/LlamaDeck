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
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static extern IntPtr objc_msgSend_NSString(IntPtr receiver, IntPtr selector, IntPtr nsString);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cString, int encoding);

    private static void SetMacOsDockIcon()
    {
        // Load the PNG from the Assets folder relative to the executable
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var pngPath = Path.Combine(baseDir, "Assets", "llama.png");
        if (!File.Exists(pngPath))
        {
            // Fallback: try project root (for dotnet run)
            pngPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "llama.png"));
            if (!File.Exists(pngPath)) return;
        }

        // NSApplication *app = [NSApplication sharedApplication];
        var nsAppClass = objc_getClass("NSApplication");
        var selSharedApp = sel_registerName("sharedApplication");
        var app = objc_msgSend(nsAppClass, selSharedApp);

        // NSString *path = [NSString stringWithUTF8String:pngPath];
        var nsString = CFStringCreateWithCString(IntPtr.Zero, pngPath, 0x08000100); // kCFStringEncodingUTF8

        // NSImage *img = [[NSImage alloc] initWithContentsOfFile:path];
        var nsImageClass = objc_getClass("NSImage");
        var selAlloc = sel_registerName("alloc");
        var img = objc_msgSend(nsImageClass, selAlloc);
        var selInitWithFile = sel_registerName("initWithContentsOfFile:");
        img = objc_msgSend_IntPtr(img, selInitWithFile, nsString);

        if (img != IntPtr.Zero)
        {
            // [app setApplicationIconImage:img];
            var selSetIcon = sel_registerName("setApplicationIconImage:");
            objc_msgSend_IntPtr(app, selSetIcon, img);
        }
    }
}
