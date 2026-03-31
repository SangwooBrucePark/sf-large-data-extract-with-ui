using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public sealed class ExecutionValidationService
{
    private readonly CredentialResolver _credentialResolver = new();

    public ExecutionValidationResult Validate(AppSettings settings, SessionState? sessionState)
    {
        var errors = new List<string>();
        ResolvedCredentials? resolvedCredentials = null;

        if (string.IsNullOrWhiteSpace(settings.SoqlTemplate))
        {
            errors.Add("SOQL template must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(settings.InClauseToken))
        {
            errors.Add("IN Clause Token must not be empty.");
        }
        else if (!string.IsNullOrWhiteSpace(settings.SoqlTemplate)
            && !settings.SoqlTemplate.Contains(settings.InClauseToken, StringComparison.Ordinal))
        {
            errors.Add("SOQL template does not contain the configured IN Clause Token.");
        }

        if (string.IsNullOrWhiteSpace(settings.KeywordsText))
        {
            errors.Add("Keyword source must not be empty.");
        }

        if (!KeywordBatchPlanner.IsSupportedDelimiter(settings.Delimiter))
        {
            errors.Add("Delimiter must be a single character or the explicit newline token \\n or \\r\\n.");
        }

        if (settings.ChunkSize <= 0)
        {
            errors.Add("Chunk Size must be a positive integer.");
        }

        if (!Enum.IsDefined(settings.ExtractionMethod))
        {
            errors.Add("Extraction Method is not supported.");
        }

        if (settings.LoginMethod == LoginMethod.Session)
        {
            var clientIdResolution = _credentialResolver.Resolve(settings.ClientId, settings.CredentialValueMode, "Client ID", required: true);
            AppendErrorIfAny(clientIdResolution, errors);

            if (settings.CallbackPort <= 0)
            {
                errors.Add("Callback Port must be a positive integer when Login Method is Session.");
            }

            if (sessionState is null || !sessionState.IsValidAt(DateTimeOffset.UtcNow))
            {
                errors.Add("Session mode requires a valid stored session state.");
            }
        }
        else
        {
            var usernameResolution = _credentialResolver.Resolve(settings.Username, settings.CredentialValueMode, "Username", required: true);
            var passwordResolution = _credentialResolver.Resolve(settings.Password, settings.CredentialValueMode, "Password", required: true);
            var tokenResolution = _credentialResolver.Resolve(settings.SecurityToken, settings.CredentialValueMode, "Security Token", required: false);

            AppendErrorIfAny(usernameResolution, errors);
            AppendErrorIfAny(passwordResolution, errors);
            AppendErrorIfAny(tokenResolution, errors);

            if (errors.Count == 0)
            {
                resolvedCredentials = new ResolvedCredentials(
                    usernameResolution.Value,
                    passwordResolution.Value,
                    tokenResolution.Value,
                    usernameResolution.Source,
                    passwordResolution.Source,
                    tokenResolution.Source);
            }
        }

        return new ExecutionValidationResult(errors.Count == 0, errors, resolvedCredentials, sessionState);
    }

    private static void AppendErrorIfAny(CredentialResolution resolution, List<string> errors)
    {
        if (!resolution.IsSuccess && !string.IsNullOrWhiteSpace(resolution.Error))
        {
            errors.Add(resolution.Error);
        }
    }
}

public sealed record ExecutionValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    ResolvedCredentials? Credentials,
    SessionState? SessionState);

public sealed record ResolvedCredentials(
    string Username,
    string Password,
    string SecurityToken,
    CredentialSource UsernameSource,
    CredentialSource PasswordSource,
    CredentialSource SecurityTokenSource);