using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public sealed class SalesforceConnectedAppLoginService
{
    private static readonly HttpClient HttpClient = new();
    private readonly CredentialResolver _credentialResolver = new();

    public async Task<SessionState> LoginAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var resolvedClientId = ResolveClientId(settings);
        ValidateSettings(settings, resolvedClientId);

        var redirectUri = BuildRedirectUri(settings.CallbackPort);
        var codeVerifier = CreateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authorizeUri = BuildAuthorizeUri(settings.LoginUrl, resolvedClientId, redirectUri, codeChallenge);

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{settings.CallbackPort}/callback/");
        listener.Start();

        OpenSystemBrowser(authorizeUri);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        var context = await WaitForCallbackAsync(listener, timeoutCts.Token);
        var query = ParseQueryString(context.Request.Url?.Query);

        if (query.TryGetValue("error", out var errorValue))
        {
            var description = query.TryGetValue("error_description", out var errorDescription)
                ? errorDescription
                : "Unknown authorization error.";

            await WriteBrowserResponseAsync(context.Response, "Login failed. You can close this window.");
            throw new InvalidOperationException($"Salesforce authorization failed: {errorValue}. {description}");
        }

        if (!query.TryGetValue("code", out var authorizationCode) || string.IsNullOrWhiteSpace(authorizationCode))
        {
            await WriteBrowserResponseAsync(context.Response, "Login failed. You can close this window.");
            throw new InvalidOperationException("Salesforce authorization did not return an authorization code.");
        }

        await WriteBrowserResponseAsync(context.Response, "Login completed. You can close this window and return to the application.");

        var tokenResponse = await ExchangeAuthorizationCodeAsync(
            settings.LoginUrl,
            resolvedClientId,
            redirectUri,
            authorizationCode,
            codeVerifier,
            cancellationToken);

        return new SessionState(
            tokenResponse.AccessToken,
            tokenResponse.InstanceUrl,
            tokenResponse.RefreshToken,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(8));
    }

    private string ResolveClientId(AppSettings settings)
    {
        var resolution = _credentialResolver.Resolve(settings.ClientId, settings.CredentialValueMode, "Client ID", required: true);
        if (!resolution.IsSuccess)
        {
            throw new InvalidOperationException(resolution.Error ?? "Client ID could not be resolved.");
        }

        return resolution.Value;
    }

    private static void ValidateSettings(AppSettings settings, string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Client ID is required for Session login.");
        }

        if (settings.CallbackPort <= 0)
        {
            throw new InvalidOperationException("Callback Port must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(settings.LoginUrl))
        {
            throw new InvalidOperationException("Login URL is required for Session login.");
        }
    }

    private static string BuildRedirectUri(int callbackPort)
    {
        return $"http://localhost:{callbackPort}/callback/";
    }

    private static string BuildAuthorizeUri(string loginUrl, string clientId, string redirectUri, string codeChallenge)
    {
        var baseUri = $"{loginUrl.TrimEnd('/')}/services/oauth2/authorize";
        var queryParameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "api refresh_token",
        };

        var queryString = string.Join("&", queryParameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{baseUri}?{queryString}";
    }

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return ToBase64Url(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return ToBase64Url(bytes);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void OpenSystemBrowser(string authorizeUri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = authorizeUri,
            UseShellExecute = true,
        });
    }

    private static async Task<HttpListenerContext> WaitForCallbackAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        var contextTask = listener.GetContextAsync();
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completedTask = await Task.WhenAny(contextTask, cancellationTask);

        if (completedTask != contextTask)
        {
            throw new OperationCanceledException("OAuth callback wait was canceled or timed out.", cancellationToken);
        }

        return await contextTask;
    }

    private static Dictionary<string, string> ParseQueryString(string? queryString)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return values;
        }

        var trimmed = queryString.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                values[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separatorIndex]);
            var value = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            values[key] = value;
        }

        return values;
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, string message)
    {
        var payload = $"<html><body><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>";
        var bytes = Encoding.UTF8.GetBytes(payload);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await using var output = response.OutputStream;
        await output.WriteAsync(bytes);
    }

    private static async Task<TokenResponse> ExchangeAuthorizationCodeAsync(
        string loginUrl,
        string clientId,
        string redirectUri,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{loginUrl.TrimEnd('/')}/services/oauth2/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["code"] = authorizationCode,
                ["code_verifier"] = codeVerifier,
            }),
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OAuth token exchange failed with status {(int)response.StatusCode}: {content}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);
        if (tokenResponse is null
            || string.IsNullOrWhiteSpace(tokenResponse.AccessToken)
            || string.IsNullOrWhiteSpace(tokenResponse.InstanceUrl))
        {
            throw new InvalidOperationException("OAuth token response was incomplete.");
        }

        return tokenResponse;
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("instance_url")] string InstanceUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken);
}