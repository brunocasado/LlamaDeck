using System;
using System.IO;
using System.Linq;

namespace LlamaSwapManager.Services;

public partial class UpdateService
{
    private bool ExtractArchive(string archivePath, string extractDirectory)
    {
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ArchiveExtractor.ExtractZip(archivePath, extractDirectory);
            else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                     archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                ArchiveExtractor.ExtractTarGz(archivePath, extractDirectory);
            else
                throw new InvalidDataException($"Unsupported update archive: {Path.GetFileName(archivePath)}");

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            LogMessage?.Invoke($"Failed to extract update archive: {ex.Message}");
            return false;
        }
    }

    internal string? FindExtractedBinary(string extractDirectory)
    {
        var binaryName = GetBinaryName();
        var exactMatch = Directory.EnumerateFiles(
                extractDirectory,
                binaryName,
                SearchOption.AllDirectories)
            .FirstOrDefault();
        if (exactMatch is not null)
            return exactMatch;

        var candidates = Directory.EnumerateFiles(
                extractDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(path => IsBinaryCandidate(path))
            .ToList();

        if (candidates.Count == 1)
            return candidates[0];

        return candidates.FirstOrDefault(path =>
                   Path.GetFileName(path).StartsWith("llama-swap", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    private bool IsBinaryCandidate(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return false;

        return _osName != "windows" || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
