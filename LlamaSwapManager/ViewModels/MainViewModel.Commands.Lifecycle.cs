using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlamaSwapManager.Models;
using LlamaSwapManager.Services;

namespace LlamaSwapManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private async Task ExecuteStartAsync()
        {
            await RunLifecycleCommandAsync(
                busyText: "Starting…",
                action: () => _processManager.StartAsync(),
                resultLabel: "start");
        }
    
        private async Task ExecuteStopAsync()
        {
            await RunLifecycleCommandAsync(
                busyText: "Stopping…",
                action: () => _processManager.StopAsync(),
                resultLabel: "stop");
        }
    
        private async Task ExecuteRestartAsync()
        {
            await RunLifecycleCommandAsync(
                busyText: "Restarting…",
                action: () => _processManager.RestartAsync(),
                resultLabel: "restart");
        }
    
        /// <summary>
        /// Shared Start/Stop/Restart plumbing: always leaves IsBusy=false and rebuilds button state,
        /// with a hard timeout so hung process I/O cannot freeze the UI forever.
        /// </summary>
        private async Task RunLifecycleCommandAsync(string busyText, Func<Task<bool>> action, string resultLabel)
        {
            IsBusy = true;
            StartButtonEnabled = false;
            StopButtonEnabled = false;
            RestartButtonEnabled = false;
            StatusText = busyText;
                ShowToast(busyText);
    
            try
            {
                // 20s ceiling covers unload + graceful + force kill; never block buttons forever.
                bool ok;
                try
                {
                    ok = await action().WaitAsync(TimeSpan.FromSeconds(20));
                }
                catch (TimeoutException)
                {
                    OnLogMessage($"[ui] {resultLabel} timed out after 20s");
                    ok = false;
                }
                OnLogMessage($"[ui] {resultLabel} result: {ok}");
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                ShowToast($"Error: {ex.Message}");
                OnLogMessage($"[ui] {resultLabel} error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                try
                {
                    await RefreshRuntimeStateAsync();
                    // Toast final runtime status (also mirrored in sidebar RUNTIME).
                    ShowToast(StatusText);
                }
                catch (Exception ex)
                {
                    OnLogMessage($"[ui] refresh after {resultLabel} failed: {ex.Message}");
                    // Force terminal UI state even if detection hangs.
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsBusy = false;
                        Status = LlamaSwapStatus.Error;
                        StatusText = $"Status: error after {resultLabel}";
                        StartButtonEnabled = true;
                        StopButtonEnabled = false;
                        RestartButtonEnabled = true;
                        ShowToast(StatusText);
                    });
                }
            }
        }
    
        private async Task RefreshRuntimeStateAsync()
        {
            try
            {
                _processManager.RefreshPaths();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                OnLogMessage($"[ui] path refresh failed: {ex.Message}");
            }
    
            // Bound detection so outer finally of lifecycle commands can always finish.
            try
            {
                var apiBaseUrl = await _processManager.DetectApiBaseUrlAsync().WaitAsync(TimeSpan.FromSeconds(3));
                if (apiBaseUrl != null)
                    _processManager.ApiBaseUrl = apiBaseUrl;
                else
                    _processManager.ApiBaseUrl = null;
            }
            catch
            {
                _processManager.ApiBaseUrl = null;
            }
    
            try
            {
                var llamaServerUrl = await _processManager.DetectLlamaServerBaseUrlAsync().WaitAsync(TimeSpan.FromSeconds(3));
                if (llamaServerUrl != null)
                    _processManager.LlamaServerBaseUrl = llamaServerUrl;
                else
                    _processManager.LlamaServerBaseUrl = null;
            }
            catch
            {
                _processManager.LlamaServerBaseUrl = null;
            }
    
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AutoDetectPaths();
                UpdateUI();
            });
        }
    
        private void ExecuteRefresh()
        {
            StatusText = "Status: refreshing...";
            OnLogMessage("[ui] refresh requested");
            ObserveRefreshAsync();
        }

        private async void ObserveRefreshAsync()
        {
            try
            {
                await RefreshRuntimeStateAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                OnLogMessage($"[ui] refresh failed: {ex.Message}");
                StatusText = "Status: refresh failed";
            }
        }
    
        public void ReportUiError(string message)
        {
            OnLogMessage($"[ui] {message}");
            ShowToast(message);
        }
    
        public void ReportUiInfo(string message)
        {
            OnLogMessage($"[ui] {message}");
            ShowToast(message);
        }
    
        /// <summary>Transient toast bubble (UI-only). Prefer this over StatusText for save/confirm feedback.</summary>
        public event Action<string>? ToastRequested;
    
        public void ShowToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            OnLogMessage($"[toast] {message}");
            ToastRequested?.Invoke(message);
        }
}
