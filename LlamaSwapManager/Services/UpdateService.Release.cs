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
    public async Task<LatestVersionInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                var releaseJson = await _releaseClient.GetLatestReleaseAsync(ct);
                if (releaseJson is null)
                {
                    LogMessage?.Invoke("Could not retrieve GitHub release information");
                    return null;
                }
    
                var release = System.Text.Json.JsonSerializer.Deserialize<JsonRelease>(
                    releaseJson.Value.GetRawText());
                if (release == null) return null;
    
                // Find the asset for this platform/arch — strict matching
                var asset = release.Assets?
                    .FirstOrDefault(a => !string.IsNullOrEmpty(a.Name) && AssetMatchesPlatform(a.Name));
    
                if (asset == null)
                {
                    LogMessage?.Invoke($"No download asset found for {_osName}/{_arch}");
                    return null;
                }
    
                var checksums = await FetchChecksumsAsync(release.TagName ?? string.Empty, ct);
    
                return new LatestVersionInfo
                {
                    Version = release.TagName,
                    AssetName = asset.Name,
                    DownloadUrl = asset.BrowserDownloadUrl,
                    SizeBytes = asset.Size,
                    Checksums = checksums
                };
            }
            catch (TaskCanceledException)
            {
                LogMessage?.Invoke("Timeout checking for updates");
                return null;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error checking for updates: {ex.Message}");
                return null;
            }
        }
    
        /// <summary>
        /// Download and install the latest llama-swap binary.
        /// Includes process stop, backup, verification, and automatic rollback.
        /// </summary>
}
