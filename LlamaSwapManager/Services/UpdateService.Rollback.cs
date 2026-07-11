using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaSwapManager.Services;

public partial class UpdateService
{
    private async Task RollbackAsync(
        string targetExecutable,
        string backupPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(backupPath))
        {
            LogMessage?.Invoke("Rollback: no backup found; manual intervention may be needed");
            return;
        }

        try
        {
            File.Move(backupPath, targetExecutable, overwrite: true);
            _platformConfigurator.SetExecutable(targetExecutable);
            await _platformConfigurator.RemoveQuarantineAsync(
                targetExecutable,
                cancellationToken);
            LogMessage?.Invoke("Rollback: restored from backup");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMessage?.Invoke($"Rollback failed: {ex.Message}");
        }
    }
}
