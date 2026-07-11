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
    private bool ExtractZip(string archivePath, string extractDir)
        {
            try
            {
                ArchiveExtractor.ExtractZip(archivePath, extractDir);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Failed to extract zip: {ex.Message}");
                return false;
            }
        }
    
        private bool ExtractTarGz(string archivePath, string extractDir)
        {
            try
            {
                ArchiveExtractor.ExtractTarGz(archivePath, extractDir);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Failed to extract tar.gz: {ex.Message}");
                return false;
            }
        }
    
        private string? FindExtractedBinary(string extractDir)
        {
            var binaryName = GetBinaryName();
            var exePattern = _osName == "windows" ? "*.exe" : "*";
    
            // First, look for the binary with the expected name
            var exactMatch = Directory.GetFiles(extractDir, binaryName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (exactMatch != null)
                return exactMatch;
    
            // Then look for any executable with the right extension
            var candidates = Directory.GetFiles(extractDir, exePattern, SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".tar.gz") && !f.EndsWith(".zip"))
                .ToList();
    
            if (candidates.Count == 1)
                return candidates[0];
    
            // If multiple candidates, prefer llama-swap or llama-server
            var preferred = candidates.FirstOrDefault(f =>
                Path.GetFileName(f).StartsWith("llama", StringComparison.OrdinalIgnoreCase));
            return preferred ?? candidates.FirstOrDefault();
        }
    
        private void SetExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
                return;
    
            try
            {
                var mode = File.GetUnixFileMode(path);
                mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(path, mode);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Warning: executable permission update failed ({ex.Message})");
            }
        }
    
        /// <summary>
        /// H1: Verify macOS codesign on a binary.
        /// Returns true if the binary is codesigned and the signature is valid.
        /// </summary>
        private bool VerifyCodesign(string path)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "codesign",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("--verify");
                psi.ArgumentList.Add("-vvvv");
                psi.ArgumentList.Add(path);
    
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
    
                var output = proc?.StandardOutput.ReadToEnd() ?? "";
                var error = proc?.StandardError.ReadToEnd() ?? "";
    
                // codesign returns 0 on success, non-zero on failure
                var success = proc?.ExitCode == 0;
                if (!success)
                {
                    LogMessage?.Invoke($"codesign verify failed for {path}: exit={proc?.ExitCode}, err={error}");
                }
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"codesign verify error: {ex.Message}");
                return false;
            }
        }
    
        /// <summary>
        /// Remove macOS quarantine attribute from a file.
        /// </summary>
        private void RemoveQuarantineAttribute(string path)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xattr",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add("com.apple.quarantine");
                psi.ArgumentList.Add(path);
    
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Warning: xattr failed ({ex.Message})");
            }
        }
    
        /// <summary>
        /// H5: Prompts the user for explicit confirmation before replacing the binary.
        /// Returns true if confirmed, false if cancelled.
        /// The actual UI dialog is shown by the caller (UpdateViewModel) before calling this method.
        /// </summary>
}
