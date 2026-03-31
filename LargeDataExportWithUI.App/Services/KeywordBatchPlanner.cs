using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public static class KeywordBatchPlanner
{
    public const int PracticalQueryLengthLimit = 18_000;

    public static KeywordAnalysis AnalyzeKeywords(string keywordsText, string delimiter)
    {
        var parsedValues = ParseKeywords(keywordsText, delimiter);

        var uniqueValues = new List<string>(parsedValues.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredCount = 0;
        var duplicateCount = 0;

        foreach (var value in parsedValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ignoredCount++;
                continue;
            }

            var trimmedValue = value.Trim();
            if (!seen.Add(trimmedValue))
            {
                duplicateCount++;
                continue;
            }

            uniqueValues.Add(trimmedValue);
        }

        return new KeywordAnalysis(
            parsedValues.Count,
            uniqueValues.Count,
            duplicateCount,
            ignoredCount,
            uniqueValues);
    }

    public static BatchPreparationResult BuildBatches(
        string soqlTemplate,
        string inClauseToken,
        string keywordsText,
        string delimiter,
        int chunkSize,
        KeywordValueType keywordValueType)
    {
        if (string.IsNullOrWhiteSpace(soqlTemplate))
        {
            throw new InvalidOperationException("SOQL template is required.");
        }

        if (string.IsNullOrWhiteSpace(inClauseToken))
        {
            throw new InvalidOperationException("IN Clause Token is required.");
        }

        if (!soqlTemplate.Contains(inClauseToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SOQL template does not contain the configured IN Clause Token.");
        }

        if (chunkSize <= 0)
        {
            throw new InvalidOperationException("Chunk Size must be greater than zero.");
        }

        var keywordAnalysis = AnalyzeKeywords(keywordsText, delimiter);
        if (keywordAnalysis.InputCount == 0)
        {
            throw new InvalidOperationException("At least one keyword is required.");
        }

        if (keywordAnalysis.UniqueValues.Count == 0)
        {
            throw new InvalidOperationException("No valid keywords remain after trimming and duplicate removal.");
        }

        var batches = new List<PreparedBatch>();
        for (var offset = 0; offset < keywordAnalysis.UniqueValues.Count;)
        {
            var remaining = keywordAnalysis.UniqueValues.Count - offset;
            var requestedCount = Math.Min(chunkSize, remaining);
            var currentCount = requestedCount;
            string? queryText = null;
            string[]? currentValues = null;

            while (currentCount > 0)
            {
                var candidateValues = keywordAnalysis.UniqueValues.Skip(offset).Take(currentCount).ToArray();
                var inClausePayload = string.Join(", ", candidateValues.Select(value => FormatSoqlLiteral(value, keywordValueType)));
                var candidateQueryText = soqlTemplate.Replace(inClauseToken, inClausePayload, StringComparison.Ordinal);

                if (candidateQueryText.Length <= PracticalQueryLengthLimit)
                {
                    currentValues = candidateValues;
                    queryText = candidateQueryText;
                    break;
                }

                currentCount--;
            }

            if (currentValues is null || queryText is null)
            {
                throw new InvalidOperationException("A single keyword item still exceeds the practical SOQL length limit.");
            }

            batches.Add(new PreparedBatch(
                batches.Count + 1,
                currentValues,
                queryText,
                queryText.Length,
                requestedCount,
                currentValues.Length < requestedCount));

            offset += currentValues.Length;
        }

        return new BatchPreparationResult(
            keywordAnalysis.InputCount,
            keywordAnalysis.UniqueCount,
            keywordAnalysis.DuplicateCount,
            keywordAnalysis.IgnoredCount,
            batches);
    }

    private static List<string> ParseKeywords(string keywordsText, string delimiter)
    {
        if (IsNewlineDelimiter(delimiter))
        {
            return [.. keywordsText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)];
        }

        var separator = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        var normalizedText = RemoveLineBreaks(keywordsText);
        return [.. normalizedText.Split(separator)];
    }

    public static bool IsSupportedDelimiter(string? delimiter)
    {
        if (string.IsNullOrWhiteSpace(delimiter))
        {
            return false;
        }

        return delimiter.Length == 1 || IsNewlineDelimiter(delimiter);
    }

    private static bool IsNewlineDelimiter(string? delimiter)
    {
        return string.Equals(delimiter, "\\n", StringComparison.Ordinal)
            || string.Equals(delimiter, "\\r\\n", StringComparison.Ordinal)
            || string.Equals(delimiter, "\n", StringComparison.Ordinal)
            || string.Equals(delimiter, "\r\n", StringComparison.Ordinal)
            || string.Equals(delimiter, "\r", StringComparison.Ordinal);
    }

    private static string RemoveLineBreaks(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == '\r' || character == '\n')
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string FormatSoqlLiteral(string value, KeywordValueType keywordValueType)
    {
        if (keywordValueType == KeywordValueType.Number)
        {
            return value;
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

        return $"'{escaped}'";
    }
}

public sealed record BatchPreparationResult(
    int InputCount,
    int UniqueCount,
    int DuplicateCount,
    int IgnoredCount,
    IReadOnlyList<PreparedBatch> Batches);

public sealed record KeywordAnalysis(
    int InputCount,
    int UniqueCount,
    int DuplicateCount,
    int IgnoredCount,
    IReadOnlyList<string> UniqueValues);

public sealed record PreparedBatch(
    int BatchNumber,
    IReadOnlyList<string> Values,
    string QueryText,
    int QueryLength,
    int RequestedItemCount,
    bool WasShrunkForQueryLimit);