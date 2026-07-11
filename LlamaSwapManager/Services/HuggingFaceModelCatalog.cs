using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaSwapManager.Services;

public sealed class HuggingFaceModelCatalog
{
    private static readonly string[] QuantizationPatterns =
    {
        "Q8_0", "Q8_K", "Q6_K", "Q5_K_M", "Q5_K_S", "Q5_0", "Q5_1",
        "Q4_K_M", "Q4_K_S", "Q4_0", "Q4_1",
        "Q3_K_M", "Q3_K_S", "Q3_K_L", "Q2_K", "Q2_0",
        "IQ4_XS", "IQ4_NL", "IQ3_XS", "IQ3_S", "IQ3_M", "IQ3_2", "IQ3_1",
        "IQ2_XS", "IQ2_S", "IQ2_M", "IQ2_2", "IQ2_1", "IQ1_S", "IQ1_M",
        "FP16", "FP8_M", "FP8_E4M3", "FP8_E5M2", "BF16", "F32", "F16"
    };

    private static readonly IReadOnlyDictionary<string, int> QuantizationOrder =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["FP8_M"] = 1, ["FP8_E4M3"] = 1, ["FP8_E5M2"] = 1,
            ["FP16"] = 2, ["BF16"] = 2, ["F16"] = 2, ["F32"] = 3,
            ["Q8_0"] = 4, ["Q8_K"] = 4, ["Q6_K"] = 5,
            ["Q5_K_M"] = 6, ["Q5_K_S"] = 6, ["Q5_0"] = 6, ["Q5_1"] = 6,
            ["Q4_K_M"] = 7, ["Q4_K_S"] = 7, ["Q4_0"] = 7, ["Q4_1"] = 7,
            ["Q3_K_M"] = 8, ["Q3_K_S"] = 8, ["Q3_K_L"] = 8,
            ["Q2_K"] = 9, ["Q2_0"] = 9,
            ["IQ4_XS"] = 10, ["IQ4_NL"] = 10,
            ["IQ3_XS"] = 11, ["IQ3_S"] = 11, ["IQ3_M"] = 11,
            ["IQ2_XS"] = 12, ["IQ2_S"] = 12,
            ["IQ1_S"] = 13, ["IQ1_M"] = 13
        };

    private readonly HttpClient _httpClient;

    public HuggingFaceModelCatalog(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<string>> SearchRepositoriesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var url = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(query)}&filter=gguf&limit=20&sort=downloads&direction=-1";
        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement
            .EnumerateArray()
            .Select(item => item.TryGetProperty("modelId", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<GgufFile>> GetGgufFilesAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var escapedId = string.Join('/', modelId.Split('/').Select(Uri.EscapeDataString));
        using var response = await GetTreeResponseAsync(escapedId, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var files = new List<GgufFile>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("path", out var pathProperty))
                continue;

            var repositoryPath = pathProperty.GetString();
            if (string.IsNullOrWhiteSpace(repositoryPath) ||
                !repositoryPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                continue;

            var quantization = ExtractQuantizationLabel(Path.GetFileName(repositoryPath));
            files.Add(new GgufFile(repositoryPath, quantization));
        }

        return files
            .OrderBy(file => file.Quantization is null ? 1 : 0)
            .ThenBy(file => GetQuantizationScore(file.Quantization))
            .ThenBy(file => file.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<HttpResponseMessage> GetTreeResponseAsync(
        string escapedModelId,
        CancellationToken cancellationToken)
    {
        var recursiveUrl = $"https://huggingface.co/api/models/{escapedModelId}/tree/main?recursive=1";
        var response = await _httpClient.GetAsync(recursiveUrl, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode != HttpStatusCode.NotFound)
            return response;

        response.Dispose();
        return await _httpClient.GetAsync(
            $"https://huggingface.co/api/models/{escapedModelId}/tree/main",
            cancellationToken);
    }

    internal static string? ExtractQuantizationLabel(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        foreach (var pattern in QuantizationPatterns)
        {
            if (baseName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                baseName.Contains($"-{pattern}", StringComparison.OrdinalIgnoreCase) ||
                baseName.Contains($"_{pattern}", StringComparison.OrdinalIgnoreCase))
                return pattern;
        }

        var match = Regex.Match(
            baseName,
            @"[-_]((UD-|IQ-)?[QIq][A-Za-z]*\d[\w_]*|FP\d+[A-Z_]*|BF\d+|F\d+[A-Z]*)$",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static int CompareQuantization(string? left, string? right) =>
        GetQuantizationScore(left).CompareTo(GetQuantizationScore(right));

    private static int GetQuantizationScore(string? quantization) =>
        quantization is not null && QuantizationOrder.TryGetValue(quantization, out var score)
            ? score
            : 50;

    public sealed record GgufFile(string RepositoryPath, string? Quantization)
    {
        public string DisplayText => Quantization is null
            ? RepositoryPath
            : $"{Quantization}  ·  {RepositoryPath}";

        public string SelectionToken => Quantization ?? RepositoryPath;
    }
}
