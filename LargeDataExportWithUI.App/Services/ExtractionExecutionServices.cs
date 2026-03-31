using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public interface IExtractionStrategy
{
    ExtractionMethod Method { get; }

    string DisplayName { get; }

    Task<BatchExecutionResult> ExecuteAsync(
        PreparedBatch batch,
        SalesforceConnectionContext connectionContext,
        ExecutionValidationResult validationResult,
        CancellationToken cancellationToken);
}

public sealed class ExtractionStrategyFactory
{
    private readonly IReadOnlyDictionary<ExtractionMethod, IExtractionStrategy> _strategies;

    public ExtractionStrategyFactory()
    {
        var strategyList = new IExtractionStrategy[]
        {
            new BulkApi2ExtractionStrategy(),
            new RestQueryExtractionStrategy(),
            new RestQueryAllExtractionStrategy(),
        };

        _strategies = strategyList.ToDictionary(strategy => strategy.Method);
    }

    public IExtractionStrategy GetStrategy(ExtractionMethod method)
    {
        if (_strategies.TryGetValue(method, out var strategy))
        {
            return strategy;
        }

        throw new InvalidOperationException($"Unsupported extraction method: {method}");
    }
}

public sealed record BatchExecutionResult(
    int RecordCount,
    string StrategyDisplayName,
    string OutcomeDetail,
    string? CsvContent);

internal abstract class SalesforceExtractionStrategyBase : IExtractionStrategy
{
    private static readonly HttpClient HttpClient = new();

    public abstract ExtractionMethod Method { get; }

    public abstract string DisplayName { get; }

    public abstract Task<BatchExecutionResult> ExecuteAsync(
        PreparedBatch batch,
        SalesforceConnectionContext connectionContext,
        ExecutionValidationResult validationResult,
        CancellationToken cancellationToken);

    protected static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string requestUri, SalesforceConnectionContext connectionContext)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connectionContext.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    protected static async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Salesforce request failed with status {(int)response.StatusCode}: {TryExtractSalesforceError(content)}");
        }

        return JsonDocument.Parse(content);
    }

    internal static string TryExtractSalesforceError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0)
            {
                var first = document.RootElement[0];
                if (first.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString() ?? "Unknown Salesforce error.";
                }
            }

            if (document.RootElement.TryGetProperty("message", out var rootMessageElement))
            {
                return rootMessageElement.GetString() ?? "Unknown Salesforce error.";
            }
        }
        catch
        {
        }

        return "Unknown Salesforce error.";
    }

    protected static int GetRecordCountFromQueryResponse(JsonDocument responseDocument)
    {
        if (responseDocument.RootElement.TryGetProperty("totalSize", out var totalSizeElement)
            && totalSizeElement.TryGetInt32(out var totalSize))
        {
            return totalSize;
        }

        if (responseDocument.RootElement.TryGetProperty("records", out var recordsElement)
            && recordsElement.ValueKind == JsonValueKind.Array)
        {
            return recordsElement.GetArrayLength();
        }

        return 0;
    }

    protected static string BuildDetail(ExecutionValidationResult validationResult)
    {
        return validationResult.Credentials is null
            ? "Used stored session context."
            : "Used resolved password credentials.";
    }

    internal static HttpClient SharedHttpClient => HttpClient;
}

internal sealed class BulkApi2ExtractionStrategy : SalesforceExtractionStrategyBase
{
    public override ExtractionMethod Method => ExtractionMethod.BulkApi2;

    public override string DisplayName => "Bulk API 2.0";

    public override async Task<BatchExecutionResult> ExecuteAsync(
        PreparedBatch batch,
        SalesforceConnectionContext connectionContext,
        ExecutionValidationResult validationResult,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{connectionContext.InstanceUrl}/services/data/v{connectionContext.ApiVersion}/jobs/query";
        using var createRequest = CreateAuthorizedRequest(HttpMethod.Post, requestUri, connectionContext);
        createRequest.Content = new StringContent(
            JsonSerializer.Serialize(new BulkQueryJobRequest("query", batch.QueryText)),
            Encoding.UTF8,
            "application/json");

        using var createResponseDocument = await SendJsonAsync(createRequest, cancellationToken);
        var jobId = createResponseDocument.RootElement.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new InvalidOperationException("Bulk API 2.0 did not return a job id.");
        }

        var statusUri = $"{requestUri}/{jobId}";
        BulkQueryJobStatus? finalStatus = null;

        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(500, cancellationToken);

            using var statusRequest = CreateAuthorizedRequest(HttpMethod.Get, statusUri, connectionContext);
            using var statusDocument = await SendJsonAsync(statusRequest, cancellationToken);
            finalStatus = JsonSerializer.Deserialize<BulkQueryJobStatus>(statusDocument.RootElement.GetRawText());

            if (finalStatus is null)
            {
                continue;
            }

            if (string.Equals(finalStatus.State, "JobComplete", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.Equals(finalStatus.State, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(finalStatus.State, "Aborted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Bulk API 2.0 job failed: {finalStatus.ErrorMessage ?? finalStatus.State}");
            }
        }

        if (finalStatus is null || !string.Equals(finalStatus.State, "JobComplete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Bulk API 2.0 job did not reach JobComplete before timeout.");
        }

        var csvContent = await DownloadBulkResultCsvAsync(connectionContext, jobId, cancellationToken);
        var resultCount = CountCsvDataRows(csvContent);
        var outcome = finalStatus.NumberRecordsProcessed > 0
            ? $"{BuildDetail(validationResult)} Bulk job processed {finalStatus.NumberRecordsProcessed} records."
            : BuildDetail(validationResult);

        return new BatchExecutionResult(resultCount, DisplayName, outcome, csvContent);
    }

    private static async Task<string> DownloadBulkResultCsvAsync(SalesforceConnectionContext connectionContext, string jobId, CancellationToken cancellationToken)
    {
        var baseUri = $"{connectionContext.InstanceUrl}/services/data/v{connectionContext.ApiVersion}/jobs/query/{jobId}/results";
        string? locator = null;
        var contentBuilder = new StringBuilder();
        var wroteHeader = false;

        do
        {
            var requestUri = string.IsNullOrWhiteSpace(locator)
                ? baseUri
                : $"{baseUri}?locator={Uri.EscapeDataString(locator)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connectionContext.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Bulk API 2.0 results request failed with status {(int)response.StatusCode}: {content}");
            }

            AppendCsvPage(contentBuilder, content, ref wroteHeader);

            locator = response.Headers.TryGetValues("Sforce-Locator", out var values)
                ? values.FirstOrDefault()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(locator) && !string.Equals(locator, "null", StringComparison.OrdinalIgnoreCase));

        return contentBuilder.ToString();
    }

    private static void AppendCsvPage(StringBuilder contentBuilder, string csvContent, ref bool wroteHeader)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return;
        }

        if (!wroteHeader)
        {
            contentBuilder.Append(csvContent);
            wroteHeader = true;
            return;
        }

        var contentWithoutHeader = RemoveFirstRow(csvContent);
        if (string.IsNullOrWhiteSpace(contentWithoutHeader))
        {
            return;
        }

        contentBuilder.Append(contentWithoutHeader.TrimStart('\r', '\n'));
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

    private static int CountCsvDataRows(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return 0;
        }

        var lines = csvContent
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count <= 1)
        {
            return 0;
        }

        return lines.Count - 1;
    }
}

internal sealed class RestQueryExtractionStrategy : SalesforceExtractionStrategyBase
{
    public override ExtractionMethod Method => ExtractionMethod.RestQuery;

    public override string DisplayName => "REST Query";

    public override async Task<BatchExecutionResult> ExecuteAsync(
        PreparedBatch batch,
        SalesforceConnectionContext connectionContext,
        ExecutionValidationResult validationResult,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{connectionContext.InstanceUrl}/services/data/v{connectionContext.ApiVersion}/query?q={Uri.EscapeDataString(batch.QueryText)}";
        var exportResult = await RestQueryCsvExporter.ExecuteRestQueryAsCsvAsync(requestUri, connectionContext, cancellationToken);
        return new BatchExecutionResult(exportResult.RecordCount, DisplayName, BuildDetail(validationResult), exportResult.CsvContent);
    }
}

internal sealed class RestQueryAllExtractionStrategy : SalesforceExtractionStrategyBase
{
    public override ExtractionMethod Method => ExtractionMethod.RestQueryAll;

    public override string DisplayName => "REST QueryAll";

    public override async Task<BatchExecutionResult> ExecuteAsync(
        PreparedBatch batch,
        SalesforceConnectionContext connectionContext,
        ExecutionValidationResult validationResult,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{connectionContext.InstanceUrl}/services/data/v{connectionContext.ApiVersion}/queryAll?q={Uri.EscapeDataString(batch.QueryText)}";
        var exportResult = await RestQueryCsvExporter.ExecuteRestQueryAsCsvAsync(requestUri, connectionContext, cancellationToken);
        return new BatchExecutionResult(exportResult.RecordCount, DisplayName, BuildDetail(validationResult), exportResult.CsvContent);
    }
}

internal sealed record RestQueryExportResult(int RecordCount, string CsvContent);

internal static class RestQueryCsvExporter
{
    public static async Task<RestQueryExportResult> ExecuteRestQueryAsCsvAsync(
        string requestUri,
        SalesforceConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, string?>>();
        var columns = new List<string>();
        string? nextRecordsPath = null;

        do
        {
            var currentRequestUri = string.IsNullOrWhiteSpace(nextRecordsPath)
                ? requestUri
                : $"{connectionContext.InstanceUrl}{nextRecordsPath}";

            using var request = new HttpRequestMessage(HttpMethod.Get, currentRequestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connectionContext.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await SalesforceExtractionStrategyBase.SharedHttpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Salesforce request failed with status {(int)response.StatusCode}: {SalesforceExtractionStrategyBase.TryExtractSalesforceError(content)}");
            }

            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("records", out var recordsElement)
                && recordsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in recordsElement.EnumerateArray())
                {
                    rows.Add(ConvertRecordToRow(record, columns));
                }
            }

            nextRecordsPath = document.RootElement.TryGetProperty("nextRecordsUrl", out var nextRecordsElement)
                ? nextRecordsElement.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(nextRecordsPath));

        return new RestQueryExportResult(rows.Count, BuildCsv(columns, rows));
    }

    private static Dictionary<string, string?> ConvertRecordToRow(JsonElement record, List<string> columns)
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var property in record.EnumerateObject())
        {
            if (string.Equals(property.Name, "attributes", StringComparison.Ordinal))
            {
                continue;
            }

            if (!columns.Contains(property.Name, StringComparer.Ordinal))
            {
                columns.Add(property.Name);
            }

            row[property.Name] = ConvertJsonValueToString(property.Value);
        }

        return row;
    }

    private static string BuildCsv(IReadOnlyList<string> columns, IReadOnlyList<Dictionary<string, string?>> rows)
    {
        if (columns.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columns.Select(EscapeCsvField)));

        foreach (var row in rows)
        {
            var values = columns.Select(column => row.TryGetValue(column, out var value) ? value ?? string.Empty : string.Empty);
            builder.AppendLine(string.Join(",", values.Select(EscapeCsvField)));
        }

        return builder.ToString();
    }

    private static string ConvertJsonValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => value.GetRawText(),
        };
    }

    private static string EscapeCsvField(string value)
    {
        if (value.Contains('"', StringComparison.Ordinal))
        {
            value = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value}\""
            : value;
    }
}

internal sealed record BulkQueryJobRequest(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("query")] string Query);

internal sealed record BulkQueryJobStatus(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("numberRecordsProcessed")] int NumberRecordsProcessed);