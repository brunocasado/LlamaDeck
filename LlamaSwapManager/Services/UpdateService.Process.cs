using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaSwapManager.Services;

/// <summary>
/// Service for downloading, verifying, and installing llama-swap updates.
/// Downloads from GitHub releases, verifies checksums, backs up the current binary,
/// and rolls back automatically on failure.
/// </summary>
public partial class UpdateService : IDisposable
{
    private async Task<bool> StopProcessAsync(CancellationToken ct)
        {
            if (_processManager is not null)
            {
                try
                {
                    return await _processManager.StopAsync();
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Process manager stop failed: {ex.Message}");
                }
            }
    
            // Fallback: try to stop via SIGTERM directly
            try
            {
                var processes = Process.GetProcessesByName("llama-swap");
                foreach (var p in processes)
                {
    
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(false); // SIGTERM
                            p.WaitForExit(5000);
                        }
                    }
                    catch { }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
}
