using System.Text;

namespace LargeDataExportWithUI.App.Services;

public sealed class CsvExportFileWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    private readonly string _filePath;
    private bool _hasWrittenContent;

    public CsvExportFileWriter(string filePath)
    {
        _filePath = filePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Output file directory is not available.");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(_filePath, string.Empty, Utf8WithoutBom, cancellationToken);
        _hasWrittenContent = false;
    }

    public async Task AppendBatchAsync(string? csvContent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return;
        }

        var contentToWrite = _hasWrittenContent
            ? TrimLeadingNewLine(RemoveFirstRow(csvContent))
            : csvContent;

        if (string.IsNullOrWhiteSpace(contentToWrite))
        {
            return;
        }

        await File.AppendAllTextAsync(_filePath, EnsureTrailingNewLine(contentToWrite), Utf8WithoutBom, cancellationToken);
        _hasWrittenContent = true;
    }

    private static string RemoveFirstRow(string content)
    {
        var newlineIndex = content.IndexOfAny(['\r', '\n']);
        if (newlineIndex < 0)
        {
            return string.Empty;
        }

        var contentStartIndex = newlineIndex + 1;
        if (content[newlineIndex] == '\r' && contentStartIndex < content.Length && content[contentStartIndex] == '\n')
        {
            contentStartIndex++;
        }

        return content[contentStartIndex..];
    }

    private static string TrimLeadingNewLine(string content)
    {
        return content.TrimStart('\r', '\n');
    }

    private static string EnsureTrailingNewLine(string content)
    {
        return content.EndsWith("\n", StringComparison.Ordinal)
            ? content
            : content + Environment.NewLine;
    }
}