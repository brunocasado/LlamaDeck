using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaSwapManager.Services;

public partial class LlamaSwapProcessManager : IDisposable
{
    public async Task<string?> GetRunningModelAsync()
        {
            var baseUrl = await DetectApiBaseUrlAsync();
            if (baseUrl is null) return null;
    
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await http.GetAsync($"{baseUrl}/running");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var modelMatch = System.Text.RegularExpressions.Regex.Match(json, "\"model\"\\s*:\\s*\"([^\"]+)\"");
                    if (modelMatch.Success)
                        return modelMatch.Groups[1].Value;
                }
            }
            catch { }
    
            return null;
        }
    
        private async Task TryUnloadModelAsync()
        {
            var baseUrl = await DetectApiBaseUrlAsync();
            if (baseUrl is null) { LogMessage?.Invoke("[manager] unload skipped: API not detected"); return; }
    
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                LogMessage?.Invoke($"[manager] unloading model via {baseUrl}/api/models/unload");
                var response = await http.PostAsync($"{baseUrl}/api/models/unload", null, cts.Token);
                LogMessage?.Invoke($"[manager] unload response: {(int)response.StatusCode} {response.StatusCode}");
                await Task.Delay(response.IsSuccessStatusCode ? 300 : 100, cts.Token);
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke("[manager] unload timed out — continuing stop");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[manager] unload failed: {ex.Message}");
            }
        }
    
        private async Task KillProcessTreeAsync()
        {
            var swapProcesses = Process.GetProcessesByName("llama-swap");
            foreach (var p in swapProcesses)
                await KillProcessTreeInternalAsync(p.Id);
    
            await Task.Delay(2000);
    
            var serverProcesses = Process.GetProcessesByName("llama-server");
            foreach (var p in serverProcesses)
                await KillProcessTreeInternalAsync(p.Id);
        }
    
        private Task KillProcessTreeInternalAsync(int processId)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = $"/F /T /PID {processId}",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    var proc = Process.Start(psi);
                    return proc?.WaitForExitAsync() ?? Task.CompletedTask;
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "pkill",
                        Arguments = $"-P {processId}",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi)?.WaitForExit();
    
                    try
                    {
                        var proc = Process.GetProcessById(processId);
                        proc.Kill(true);
                    }
                    catch { }
                }
            }
            catch { }
    
            return Task.CompletedTask;
        }
    
        private async Task<bool> WaitForStoppedAsync(int timeoutSeconds)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline)
            {
                var swapRunning = Process.GetProcessesByName("llama-swap").Length > 0;
                var serverRunning = Process.GetProcessesByName("llama-server").Length > 0;
                if (!swapRunning && !serverRunning)
                    return true;
                await Task.Delay(250);
            }
            return Process.GetProcessesByName("llama-swap").Length == 0 &&
                   Process.GetProcessesByName("llama-server").Length == 0;
        }
    
        private async Task<bool> WaitForApiReadyAsync(int timeoutSeconds)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline)
            {
                var baseUrl = await DetectApiBaseUrlAsync();
                if (baseUrl is not null)
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                        var response = await http.GetAsync($"{baseUrl}/running");
                        if (response.IsSuccessStatusCode)
                            return true;
                    }
                    catch { }
                }
                await Task.Delay(500);
            }
            return false;
        }
    
        private async Task ForceKillAsync()
        {
            // H3: Only kill managed PIDs
            int[] managedPids;
            lock (_managedPids)
            {
                managedPids = _managedPids.ToArray();
            }
            foreach (var pid in managedPids)
            {
                try { var p = Process.GetProcessById(pid); p.Kill(true); } catch { }
            }
            await Task.Delay(1000);
            SetStatus(LlamaSwapStatus.Stopped);
        }
    
        public async Task<string?> DetectApiBaseUrlAsync()
        {
            // llama-swap default port — just test it directly
            if (await TestEndpointAsync("http://127.0.0.1:8080"))
                return "http://127.0.0.1:8080";
    
            // Fallback: try to find llama-swap process and detect port
            var swapProcesses = Process.GetProcessesByName("llama-swap");
            foreach (var swap in swapProcesses)
            {
                try
                {
                    var ports = GetListeningPorts(swap.Id);
                    foreach (var port in ports)
                    {
                        var baseUrl = $"http://127.0.0.1:{port}";
                        if (await TestEndpointAsync(baseUrl))
                            return baseUrl;
                    }
                }
                catch { }
            }
    
            return null;
        }
    
        /// <summary>
        /// Detects the upstream llama-server URL by querying llama-swap's /running endpoint.
        /// The /running response includes a "proxy" field with the llama-server address.
        /// Falls back to port scanning if llama-swap is unreachable or no model is loaded.
        /// </summary>
        public async Task<string?> DetectLlamaServerBaseUrlAsync()
        {
            // Primary: query llama-swap /running for the upstream proxy URL
            var swapBaseUrl = await DetectApiBaseUrlAsync();
            if (swapBaseUrl is not null)
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                    var response = await http.GetAsync($"{swapBaseUrl}/running");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        // Extract "proxy" from the first running entry:
                        // {"running":[{"model":"...","proxy":"http://localhost:5801",...}]}
                        var proxyMatch = System.Text.RegularExpressions.Regex.Match(
                            json,
                            "\"proxy\"\\s*:\\s*\"([^\"]+)\"");
                        if (proxyMatch.Success)
                        {
                            var proxyUrl = proxyMatch.Groups[1].Value;
                            // Normalize: llama-swap returns "localhost", ensure we use 127.0.0.1
                            proxyUrl = proxyUrl.Replace("localhost", "127.0.0.1");
                            return proxyUrl;
                        }
                    }
                }
                catch { }
            }
    
            // Fallback: port scan 5800-5900 for llama-server /health
            for (var port = 5800; port <= 5900; port++)
            {
                var baseUrl = $"http://127.0.0.1:{port}";
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                    var response = await http.GetAsync($"{baseUrl}/health");
                    if (response.IsSuccessStatusCode)
                        return baseUrl;
                }
                catch { }
            }
    
            return null;
        }
    
        private HashSet<int> GetListeningPorts(int processId)
        {
            var ports = new HashSet<int>();
    
            try
            {
         if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"Get-NetTCPConnection -OwningProcess {processId} -State Listen | Select-Object -ExpandProperty LocalPort",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    // Guard against infinite hang: 5s timeout on the whole operation.
                    var exited = proc?.WaitForExit(5000) ?? false;
                    if (!exited)
                    {
                        try { proc?.Kill(true); } catch { }
                        return ports;
                    }
                    var output = proc?.StandardOutput.ReadToEnd();
                    if (output is not null)
                    {
                        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (int.TryParse(line.Trim(), out var port))
                                ports.Add(port);
                        }
                    }
                }
                else
                {
                    // CRITICAL: lsof -p on macOS returns ALL system ports, not just for the process.
                    // We must filter by the COMMAND column matching the target process name.
                    var psi = new ProcessStartInfo
                    {
                        FileName = "lsof",
                        Arguments = $"-iTCP -sTCP:LISTEN -p {processId} -nP",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd();
                    if (output is not null)
                    {
                        foreach (var line in output.Split('\n'))
                        {
                            // Only accept lines that start with the target process name
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length < 1) continue;
                            var cmd = parts[0];
                            // Filter: only lines where COMMAND matches the target process
                            if (cmd != "llama-swap" && cmd != "llama-server" && cmd != "LlamaSwapManager.Desktop")
                                continue;
    
                            // Now extract port from the NAME column (last field)
                            var namePart = parts[^1];
                            if ((namePart.StartsWith("*:") || namePart.StartsWith("127.0.0.1:")) && namePart.Contains(':'))
                            {
                                var portStr = namePart.Split(':')[^1];
                                if (int.TryParse(portStr, out var port))
                                    ports.Add(port);
                            }
                        }
                    }
                }
            }
            catch { }
    
            return ports;
        }
    
        private async Task<bool> TestEndpointAsync(string baseUrl)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await http.GetAsync($"{baseUrl}/running");
                return response.IsSuccessStatusCode;
            }
            catch { }
            return false;
        }
    
        private void SetStatus(LlamaSwapStatus newStatus)
        {
            lock (_lock)
            {
                if (Status != newStatus)
                {
                    Status = newStatus;
                    StatusChanged?.Invoke(Status);
                }
            }
        }
    
        private void OnProcessExited(object? sender, EventArgs e)
        {
            // H3: Remove this PID from managed list
            if (_process is not null)
            {
                lock (_managedPids)
                {
                    _managedPids.Remove(_process.Id);
                }
            }
    
            SetStatus(LlamaSwapStatus.Stopped);
            ApiBaseUrl = null;
        }
    
        public bool IsRunning()
        {
            return Process.GetProcessesByName("llama-swap").Length > 0 ||
                   Process.GetProcessesByName("llama-server").Length > 0;
        }
    
        public bool IsLlamaSwapProcessRunning()
        {
            return Process.GetProcessesByName("llama-swap").Length > 0;
        }
    
        public bool IsProxyRunning()
        {
            return Process.GetProcessesByName("llama-swap").Length > 0 || ApiBaseUrl is not null;
        }
    
        public void Dispose() { }
}
