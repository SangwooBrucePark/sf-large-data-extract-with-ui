using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public sealed class CredentialResolver
{
    private const string EnvironmentPrefix = "env:";

    public CredentialResolution Resolve(string rawValue, CredentialValueSource valueSource, string fieldName, bool required)
    {
        if (valueSource == CredentialValueSource.EnvironmentVariable)
        {
            return ResolveEnvironmentVariable(rawValue, fieldName, required);
        }

        if (!string.IsNullOrWhiteSpace(rawValue) && rawValue.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveEnvironmentVariable(rawValue[EnvironmentPrefix.Length..], fieldName, required);
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return required
                ? CredentialResolution.Failure($"{fieldName} is required.")
                : CredentialResolution.Success(string.Empty, CredentialSource.Empty);
        }

        return CredentialResolution.Success(rawValue, CredentialSource.DirectValue);
    }

    public CredentialResolution Resolve(string rawValue, string fieldName, bool required)
    {
        return Resolve(rawValue, CredentialValueSource.Custom, fieldName, required);
    }

    private static CredentialResolution ResolveEnvironmentVariable(string rawValue, string fieldName, bool required)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return required
                ? CredentialResolution.Failure($"{fieldName} is required.")
                : CredentialResolution.Success(string.Empty, CredentialSource.Empty);
        }

        var variableName = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return CredentialResolution.Failure($"{fieldName} environment variable name is empty.");
        }

        var resolvedValue = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(resolvedValue))
        {
            return CredentialResolution.Failure($"{fieldName} environment variable could not be resolved: {variableName}");
        }

        return CredentialResolution.Success(resolvedValue, CredentialSource.EnvironmentVariable);
    }
}

public sealed record CredentialResolution(string Value, CredentialSource Source, string? Error)
{
    public bool IsSuccess => string.IsNullOrWhiteSpace(Error);

    public static CredentialResolution Success(string value, CredentialSource source)
    {
        return new CredentialResolution(value, source, null);
    }

    public static CredentialResolution Failure(string error)
    {
        return new CredentialResolution(string.Empty, CredentialSource.Empty, error);
    }
}

public enum CredentialSource
{
    Empty,
    DirectValue,
    EnvironmentVariable,
}