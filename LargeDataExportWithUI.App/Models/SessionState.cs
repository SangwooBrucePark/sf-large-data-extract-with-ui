namespace LargeDataExportWithUI.App.Models;

public sealed record SessionState(
    string AccessToken,
    string? InstanceUrl,
    string? RefreshToken,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc)
{
    public bool IsValidAt(DateTimeOffset timestampUtc)
    {
        return !string.IsNullOrWhiteSpace(AccessToken)
            && !string.IsNullOrWhiteSpace(InstanceUrl)
            && ExpiresUtc > timestampUtc;
    }
}