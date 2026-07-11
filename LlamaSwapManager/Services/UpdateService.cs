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
    private const string GitHubRepo = "mostlygeek/llama-swap";
    private const int ApiTimeoutSeconds = 30;
    private const long MinDiskSpaceBytes = 500L * 1024 * 1024; // 500MB minimum free

    private readonly HttpClient _httpClient;
    private readonly GitHubReleaseClient _releaseClient;
    private readonly string _installDirectory;
    private readonly string _osName;
    private readonly string _arch;
    private readonly LlamaSwapProcessManager? _processManager;
    private readonly SemaphoreSlim _updateCheckLock = new(1, 1);
    private static DateTime _lastUpdateCheck = DateTime.MinValue;
    private static readonly TimeSpan UpdateCheckCooldown = TimeSpan.FromMinutes(5);

    public event Action<UpdateProgress>? ProgressChanged;
    public event Action<string>? LogMessage;

    /// <summary>
    /// Creates an UpdateService for the given install directory.
    /// </summary>
    /// <param name="installDirectory">Directory where llama-swap binary lives.</param>
    /// <param name="processManager">Optional process manager for stop/start integration.</param>
    public UpdateService(string installDirectory, LlamaSwapProcessManager? processManager = null)
    {
        _installDirectory = installDirectory;
        _processManager = processManager;
        _osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" :
                   RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
                   RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "linux";
        _arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "amd64",
            Architecture.X86 => "amd64",
            _ => "amd64"
        };

        _httpClient = CreateSecureHttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LlamaSwapManager/1.0");

        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".llama-swap",
            ".updates",
            "github-cache");
        _releaseClient = new GitHubReleaseClient(
            _httpClient,
            GitHubRepo,
            cacheDirectory,
            message => LogMessage?.Invoke(message));
    }

    /// <summary>
    /// Creates an HttpClient with explicit security settings:
    /// - Server certificate validation enabled (no dangerous bypass)
    /// - Optional GitHub token for authentication (M2: GitHub API auth)
    /// </summary>
    private static HttpClient CreateSecureHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // Reject if there are any SSL policy errors
                if (errors != SslPolicyErrors.None)
                {
                    return false;
                }
                return true;
            }
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds) };

        // M2: Support GITHUB_TOKEN env var for authenticated API calls (60/hr → 5000/hr)
        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(githubToken))
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", githubToken);
        }

        return client;
    }

    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    /// Check for the latest available version without downloading.
    /// </summary>
}
