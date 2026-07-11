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
    private async Task<bool> DownloadWithProgressAsync(string url, string destination, long expectedSize, CancellationToken ct)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    LogMessage?.Invoke($"Download failed: {(int)response.StatusCode} {response.StatusCode}");
                    return false;
                }
    
                var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
    
                var buffer = new byte[81920];
                var totalRead = 0L;
                var lastProgressReport = 0;
    
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
    
                    var bytesRead = await stream.ReadAsync(buffer, ct);
                    if (bytesRead == 0) break;
    
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
    
                    // Report progress every ~5% or at significant milestones
                    var progressPct = totalBytes > 0 ? (int)((totalRead * 100) / totalBytes) : 0;
                    if (progressPct - lastProgressReport >= 5 || progressPct == 100)
                    {
                        lastProgressReport = progressPct;
                        ProgressChanged?.Invoke(new UpdateProgress(
                            $"Downloading... {FormatBytes(totalRead)} / {FormatBytes(totalBytes)}",
                            20 + (progressPct * 40 / 100))); // 20-60% range
                    }
                }
    
                // Verify file size (allow ±1% tolerance for GitHub CDN inconsistencies)
                var fileInfo = new FileInfo(destination);
                if (totalBytes > 0 && Math.Abs(fileInfo.Length - totalBytes) > totalBytes * 0.01)
                {
                    LogMessage?.Invoke($"File size mismatch: expected {totalBytes}, got {fileInfo.Length}");
                    return false;
                }
    
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Download error: {ex.Message}");
                return false;
            }
        }
}
