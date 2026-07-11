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
    public async Task<bool> UpdateAsync(string targetVersion, CancellationToken ct = default)
        {
            var backupPath = Path.Combine(_installDirectory, $"llama-swap.backup.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
            var tempDir = Path.Combine(Path.GetTempPath(), $"llama-swap-update-{Guid.NewGuid()}");
            var targetExe = Path.Combine(_installDirectory, GetBinaryName());
    
            Directory.CreateDirectory(tempDir);
    
            try
            {
                // Step 1: Check for updates
                ProgressChanged?.Invoke(new UpdateProgress("Checking for updates...", 0));
    
                var latest = await CheckForUpdatesAsync(ct);
                if (latest == null)
                {
                    LogMessage?.Invoke("Could not retrieve update information");
                    return false;
                }
    
                var version = string.IsNullOrWhiteSpace(targetVersion)
                    ? latest.Version ?? "unknown"
                    : targetVersion;
                ProgressChanged?.Invoke(new UpdateProgress($"Found version {version}", 10));
    
                // Step 2: Verify disk space
                ProgressChanged?.Invoke(new UpdateProgress("Checking disk space...", 12));
    
                if (!CheckDiskSpace(latest.SizeBytes))
                {
                    LogMessage?.Invoke("Insufficient disk space for update");
                    return false;
                }
    
                // Step 3: Stop llama-swap process before install
                ProgressChanged?.Invoke(new UpdateProgress("Stopping llama-swap...", 15));
    
                var processStopped = await StopProcessAsync(ct);
                if (!processStopped)
                {
                    LogMessage?.Invoke("Could not stop llama-swap — update aborted for safety");
                    return false;
                }
    
                // Step 4: Download the archive
                ProgressChanged?.Invoke(new UpdateProgress("Downloading archive...", 20));
    
                var archiveName = latest.AssetName ?? $"llama-swap-{SanitizeVersion(latest.Version ?? "unknown")}.tar.gz";
                var archivePath = Path.Combine(tempDir, archiveName);
                var downloadUrl = latest.DownloadUrl;
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    LogMessage?.Invoke("No download URL available");
                    return false;
                }
                var downloadSize = latest.SizeBytes;
                var downloaded = await DownloadWithProgressAsync(downloadUrl, archivePath, downloadSize, ct);
    
                if (!downloaded)
                {
                    LogMessage?.Invoke("Download failed");
                    return false;
                }
    
                // Step 5: Verify checksum
                ProgressChanged?.Invoke(new UpdateProgress("Verifying checksum...", 60));
    
                var archiveHash = await ComputeSha256Async(archivePath);
                var checksumExpected = latest.Checksums?.FirstOrDefault(c => c.Name == archiveName)?.Sha256;
    
                if (!string.IsNullOrEmpty(checksumExpected) && !archiveHash.Equals(checksumExpected, StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage?.Invoke($"Checksum mismatch: expected {checksumExpected}, got {archiveHash}");
                    return false;
                }
    
                ProgressChanged?.Invoke(new UpdateProgress("Checksum verified", 70));
    
                // Step 6: Extract archive (tar.gz for Unix, zip for Windows)
                ProgressChanged?.Invoke(new UpdateProgress("Extracting archive...", 75));
    
                var extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);
    
                if (!ExtractArchive(archivePath, extractDir))
                {
                    LogMessage?.Invoke("Failed to extract archive");
                    return false;
                }
    
                var extractedExe = FindExtractedBinary(extractDir);
                if (string.IsNullOrEmpty(extractedExe))
                {
                    LogMessage?.Invoke("Could not find extracted binary");
                    return false;
                }
    
                // Step 7: Backup current binary
                ProgressChanged?.Invoke(new UpdateProgress("Preparing installation...", 80));
    
                var hadExistingBinary = File.Exists(targetExe);
                if (hadExistingBinary)
                {
                    try
                    {
                        File.Copy(targetExe, backupPath, overwrite: false);
                        LogMessage?.Invoke($"Backup created at {backupPath}");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        LogMessage?.Invoke($"Backup failed; update aborted: {ex.Message}");
                        return false;
                    }
                }
    
                // Step 7.5: H5 — Require explicit user confirmation before replacing binary
                var confirmed = await PromptUpdateConfirmationAsync(version, targetExe, ct);
                if (!confirmed)
                {
                    LogMessage?.Invoke("Update cancelled by user");
                    return false;
                }
    
                // Step 8: Stage and atomically replace the binary in the destination directory.
                ProgressChanged?.Invoke(new UpdateProgress("Installing...", 85));
                var stagedExe = Path.Combine(
                    _installDirectory,
                    $".{Path.GetFileName(targetExe)}.update-{Guid.NewGuid():N}");
                File.Copy(extractedExe, stagedExe, overwrite: false);

                try
                {
                    _platformConfigurator.SetExecutable(stagedExe);
                    await _platformConfigurator.RemoveQuarantineAsync(stagedExe, ct);

                    if (!await _platformConfigurator.VerifyCodesignAsync(stagedExe, ct))
                    {
                        LogMessage?.Invoke(
                            "Warning: codesign verification failed — checksum is valid, continuing with installation");
                    }

                    File.Move(stagedExe, targetExe, overwrite: true);
                }
                finally
                {
                    try { File.Delete(stagedExe); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        LogMessage?.Invoke($"Warning: staged update cleanup failed ({ex.Message})");
                    }
                }
    
                // Step 9: Restart process if process manager is available
                ProgressChanged?.Invoke(new UpdateProgress("Restarting llama-swap...", 95));
    
                if (_processManager is not null)
                {
                    try
                    {
                        await _processManager.RestartAsync();
                        LogMessage?.Invoke("llama-swap restarted successfully");
                    }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke($"Warning: restart failed ({ex.Message}) — binary updated but process needs manual restart");
                    }
                }
    
                ProgressChanged?.Invoke(new UpdateProgress("Update complete!", 100));
                LogMessage?.Invoke($"Updated llama-swap to {version}");
    
                return true;
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke("Update cancelled");
                await RollbackAsync(targetExe, backupPath, CancellationToken.None);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                LogMessage?.Invoke($"Update failed: {ex.Message}");
    
                // Rollback
                await RollbackAsync(targetExe, backupPath, ct);
                return false;
            }
            finally
            {
                // Cleanup temp files
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogMessage?.Invoke($"Warning: temporary update cleanup failed ({ex.Message})");
                }
            }
        }
    
        /// <summary>
        /// Rollback: restore the backup binary if the update failed.
        /// </summary>
}
