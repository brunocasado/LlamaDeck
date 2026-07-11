using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaSwapManager.Services;

public partial class UpdateService
{
    private async Task<bool> StopProcessAsync(CancellationToken cancellationToken)
    {
        if (_processManager is not null)
        {
            try
            {
                return await _processManager.StopAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                LogMessage?.Invoke($"Process manager stop failed: {ex.Message}");
            }
        }

        var expectedPath = Path.Combine(_installDirectory, GetBinaryName());
        var matchedAny = false;
        foreach (var process in Process.GetProcessesByName("llama-swap"))
        {
            using (process)
            {
                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (!LlamaSwapProcessManager.IsExpectedExecutable(processPath, expectedPath))
                        continue;

                    matchedAny = true;
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(cancellationToken)
                            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException or
                    TimeoutException)
                {
                    LogMessage?.Invoke($"Fallback stop failed for pid={process.Id}: {ex.Message}");
                    return false;
                }
            }
        }

        return !matchedAny || !File.Exists(expectedPath) || !IsExpectedProcessRunning(expectedPath);
    }

    private static bool IsExpectedProcessRunning(string expectedPath)
    {
        foreach (var process in Process.GetProcessesByName("llama-swap"))
        {
            using (process)
            {
                try
                {
                    if (LlamaSwapProcessManager.IsExpectedExecutable(
                            process.MainModule?.FileName,
                            expectedPath))
                        return true;
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
                {
                    // The process may have exited while being inspected.
                }
            }
        }

        return false;
    }
}
