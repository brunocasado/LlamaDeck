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
    private async Task<IReadOnlyList<ChecksumEntry>?> FetchChecksumsAsync(string tagName, CancellationToken ct)
        {
            try
            {
                var checksumUrl = $"https://github.com/{GitHubRepo}/releases/download/{tagName}/llama-swap_{SanitizeVersion(tagName)}_checksums.txt";
                var response = await _httpClient.GetAsync(checksumUrl, ct);
    
                if (!response.IsSuccessStatusCode)
                    return null;
    
                var content = await response.Content.ReadAsStringAsync(ct);
                var checksums = new List<ChecksumEntry>();
    
                foreach (var line in content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // Format: <hash>  <filename>  or  <hash>  *<filename>
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[0].Length == 64) // SHA-256 hex
                    {
                        var hash = parts[0];
                        var fileName = parts[1].TrimStart('*');
                        checksums.Add(new ChecksumEntry { Name = fileName, Sha256 = hash });
                    }
                }
    
                return checksums.Count > 0 ? (IReadOnlyList<ChecksumEntry>)checksums : null;
            }
            catch
            {
                return null;
            }
        }
}
