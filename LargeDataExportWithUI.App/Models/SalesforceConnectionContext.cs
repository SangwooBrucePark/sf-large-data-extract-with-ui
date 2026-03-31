namespace LargeDataExportWithUI.App.Models;

public sealed record SalesforceConnectionContext(
    string InstanceUrl,
    string AccessToken,
    LoginMethod LoginMethod,
    string ApiVersion);