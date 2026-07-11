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
    private void RollbackAsync(string targetExe, string backupPath)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    if (File.Exists(targetExe))
                        File.Delete(targetExe);
    
                    File.Move(backupPath, targetExe);
                    SetExecutable(targetExe);
                    LogMessage?.Invoke("Rollback: restored from backup");
    
                    if (_osName == "darwin")
                    {
                        RemoveQuarantineAttribute(targetExe);
                    }
                }
                else
                {
                    LogMessage?.Invoke("Rollback: no backup found, manual intervention may be needed");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Rollback failed: {ex.Message}");
            }
        }
    
        /// <summary>
        /// Stop llama-swap via the process manager (if available), or via SIGTERM directly.
        /// </summary>
}
