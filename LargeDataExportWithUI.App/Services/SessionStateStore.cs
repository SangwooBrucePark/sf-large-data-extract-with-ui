using System.Security.Cryptography;
using System.Text.Json;
using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public sealed class SessionStateStore
{
    private readonly string _filePath;

    public SessionStateStore()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LargeDataExportWithUI");

        _filePath = Path.Combine(appDataDirectory, "session.bin");
    }

    public async Task<SessionState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_filePath, cancellationToken);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<SessionState>(plainBytes);
    }

    public async Task SaveAsync(SessionState sessionState, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Session state directory is not available.");

        Directory.CreateDirectory(directory);

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(sessionState);
        var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, protectedBytes, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }
}