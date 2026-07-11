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
    private Task<bool> PromptUpdateConfirmationAsync(string version, string targetPath, CancellationToken ct)
        {
            // UI layer handles the confirmation dialog.
            // If we reach here, the user has already confirmed.
            LogMessage?.Invoke($"Update to {version} confirmed by user");
            return Task.FromResult(true);
        }
    
        /// <summary>
        /// M6: Rate limiting for update checks.
        /// Checks if an update check is allowed based on the cooldown period.
        /// Returns true if allowed, false if throttled.
        /// </summary>
        private async Task<bool> TryAcquireUpdateCheckLockAsync(CancellationToken ct)
        {
            try
            {
                // Check cooldown first (static, shared across instances)
                if ((DateTime.UtcNow - _lastUpdateCheck) < UpdateCheckCooldown)
                {
                    return false;
                }
    
                // Use semaphore to prevent concurrent checks across instances
                var acquired = await _updateCheckLock.WaitAsync(TimeSpan.FromSeconds(1), ct);
                if (!acquired)
                {
                    return false;
                }
    
                // Double-check cooldown after acquiring lock
                if ((DateTime.UtcNow - _lastUpdateCheck) < UpdateCheckCooldown)
                {
                    _updateCheckLock.Release();
                    return false;
                }
    
                _lastUpdateCheck = DateTime.UtcNow;
                return true;
            }
            catch
            {
                return true; // Fail open — don't block update checks on lock errors
            }
        }
    
        public void ReleaseUpdateCheckLock()
        {
            _updateCheckLock.Release();
        }
    
        private bool CheckDiskSpace(long requiredBytes)
        {
            try
            {
                // Get free space on the drive where the install directory is located
                var drive = new DriveInfo(Path.GetPathRoot(_installDirectory) ?? "/");
                var freeBytes = drive.AvailableFreeSpace;
    
                // Require at least the requested space or the minimum, whichever is greater
                var required = Math.Max(requiredBytes, MinDiskSpaceBytes);
                return freeBytes >= required;
            }
            catch
            {
                return true; // Fail open — don't block update on disk check errors
            }
        }
    
        private bool AssetMatchesPlatform(string assetName)
        {
            if (_osName == "darwin")
                // GitHub uses "macos" in asset names, not "darwin"
                return (assetName.Contains("darwin", StringComparison.OrdinalIgnoreCase) ||
                        assetName.Contains("macos", StringComparison.OrdinalIgnoreCase)) &&
                       assetName.Contains(_arch, StringComparison.OrdinalIgnoreCase);
    
            if (_osName == "windows")
                return assetName.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                       assetName.Contains(_arch, StringComparison.OrdinalIgnoreCase);
    
            // Linux
            return assetName.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
                   assetName.Contains(_arch, StringComparison.OrdinalIgnoreCase);
        }
    
        private string GetBinaryName()
        {
            return _osName == "windows" ? "llama-swap.exe" : "llama-swap";
        }
    
        private static string SanitizeVersion(string version)
        {
            // Remove leading 'v' if present
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                version = version.Substring(1);
    
            // Replace any characters that are not safe for URLs
            return System.Text.RegularExpressions.Regex.Replace(version, @"[^a-zA-Z0-9._-]", "_");
        }
    
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }
    
        private async Task<string> ComputeSha256Async(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var hashBytes = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    
    // =====================================================================
        // Data structures
        // =====================================================================
    
        public class LatestVersionInfo
        {
            public string? Version { get; init; }
            public string? AssetName { get; init; }
            public string? DownloadUrl { get; init; }
            public long SizeBytes { get; init; }
            public IReadOnlyList<ChecksumEntry>? Checksums { get; init; }
        }
    
        public class ChecksumEntry
        {
            public string? Name { get; init; }
            public string? Sha256 { get; init; }
        }
    
        public class JsonRelease
        {
            [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
            public string? TagName { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("assets")]
            public JsonAsset[]? Assets { get; set; }
        }
    
        public class JsonAsset
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string? Name { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("size")]
            public long Size { get; set; }
        }
    
        public class UpdateProgress
        {
            public string Message { get; }
            public int Percentage { get; }
    
            public UpdateProgress(string message, int percentage)
            {
                Message = message;
                Percentage = percentage;
            }
        }
}
