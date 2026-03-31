using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly string _filePath;

    public AppSettingsStore()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LargeDataExportWithUI");

        _filePath = Path.Combine(appDataDirectory, "appsettings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_filePath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.EnsureDefaults();

        var fileBytes = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Settings directory is not available.");

        Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(_filePath, fileBytes, cancellationToken);
    }

    public void Save(AppSettings settings)
    {
        settings.EnsureDefaults();

        var fileBytes = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Settings directory is not available.");

        Directory.CreateDirectory(directory);

        File.WriteAllBytes(_filePath, fileBytes);
    }
}