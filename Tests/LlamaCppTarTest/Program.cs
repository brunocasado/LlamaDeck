using System;
using System.IO;
using System.Linq;
using LlamaSwapManager.Services;

class Program
{
    static void Main()
    {
        var archivePath = "/tmp/llama-test.tar.gz";
        var extractDir = "/tmp/extract-test-cs";
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);

        // Production extraction path
        ArchiveExtractor.ExtractTarGz(archivePath, extractDir);

        var all = Directory.EnumerateFileSystemEntries(extractDir).ToList();
        var files = Directory.EnumerateFiles(extractDir).ToList();
        var dirs = Directory.EnumerateDirectories(extractDir).ToList();
        var symlinks = Directory.EnumerateFileSystemEntries(extractDir)
            .Where(p => File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)).ToList();
        var mangled = files.Where(f =>
            Path.GetFileName(f).Contains("__") ||
            Path.GetFileName(f).All(c => char.IsDigit(c))).ToList();

        Console.WriteLine($"Total entries: {all.Count}");
        Console.WriteLine($"Files: {files.Count}, Dirs: {dirs.Count}, Symlinks: {symlinks.Count}");
        Console.WriteLine($"Mangled/junk entries: {mangled.Count}");
        Console.WriteLine($"llama-server present: {File.Exists(Path.Combine(extractDir, "llama-server"))}");
        Console.WriteLine($"libllama.dylib symlink ok: {File.Exists(Path.Combine(extractDir, "libllama.dylib"))}");

        // Verify a symlink resolves correctly
        var lib = Path.Combine(extractDir, "libllama.dylib");
        if (File.Exists(lib))
        {
            var info = new FileInfo(lib);
            Console.WriteLine($"libllama.dylib resolves to: {info.FullName} ({info.Length} bytes)");
        }

        // Run the version check on the extracted binary
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(extractDir, "llama-server"), "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = System.Diagnostics.Process.Start(psi);
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        Console.WriteLine($"--version output: {output.Trim().Split('\n')[0]}");
    }
}
