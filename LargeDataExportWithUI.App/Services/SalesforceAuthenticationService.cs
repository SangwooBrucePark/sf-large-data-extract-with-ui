using System.Net.Http.Headers;
using System.Security;
using System.Xml.Linq;
using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public sealed class SalesforceAuthenticationService
{
    public const string DefaultApiVersion = "61.0";

    private static readonly HttpClient HttpClient = new();

    public async Task<SalesforceConnectionContext> CreateConnectionAsync(
        AppSettings settings,
        ExecutionValidationResult validationResult,
        CancellationToken cancellationToken)
    {
        if (settings.LoginMethod == LoginMethod.Session)
        {
            var sessionState = validationResult.SessionState
                ?? throw new InvalidOperationException("Session state is not available.");

            if (!sessionState.IsValidAt(DateTimeOffset.UtcNow))
            {
                throw new InvalidOperationException("Stored session state is not valid anymore.");
            }

            return new SalesforceConnectionContext(
                sessionState.InstanceUrl!,
                sessionState.AccessToken,
                LoginMethod.Session,
                DefaultApiVersion);
        }

        var credentials = validationResult.Credentials
            ?? throw new InvalidOperationException("Resolved credentials are required for password authentication.");

        return await AuthenticateWithPasswordAsync(settings, credentials, cancellationToken);
    }

    private static async Task<SalesforceConnectionContext> AuthenticateWithPasswordAsync(
        AppSettings settings,
        ResolvedCredentials credentials,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{settings.LoginUrl.TrimEnd('/')}/services/Soap/u/{DefaultApiVersion}";
        var passwordValue = credentials.Password + credentials.SecurityToken;
        var escapedUsername = SecurityElement.Escape(credentials.Username) ?? string.Empty;
        var escapedPassword = SecurityElement.Escape(passwordValue) ?? string.Empty;

        var soapEnvelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <env:Envelope xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:env="http://schemas.xmlsoap.org/soap/envelope/">
              <env:Body>
                <n1:login xmlns:n1="urn:partner.soap.sforce.com">
                  <n1:username>{escapedUsername}</n1:username>
                  <n1:password>{escapedPassword}</n1:password>
                </n1:login>
              </env:Body>
            </env:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(soapEnvelope, System.Text.Encoding.UTF8, "text/xml"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.Add("SOAPAction", "login");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Salesforce login request failed with status {(int)response.StatusCode}: {TryExtractSoapFault(responseContent)}");
        }

        var document = XDocument.Parse(responseContent);
        XNamespace partnerNs = "urn:partner.soap.sforce.com";

        var sessionId = document.Descendants(partnerNs + "sessionId").FirstOrDefault()?.Value;
        var serverUrl = document.Descendants(partnerNs + "serverUrl").FirstOrDefault()?.Value;

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new InvalidOperationException("Salesforce login response did not contain sessionId or serverUrl.");
        }

        var serverUri = new Uri(serverUrl, UriKind.Absolute);
        var instanceUrl = serverUri.GetLeftPart(UriPartial.Authority);

        return new SalesforceConnectionContext(
            instanceUrl,
            sessionId,
            LoginMethod.Password,
            DefaultApiVersion);
    }

    private static string TryExtractSoapFault(string responseContent)
    {
        try
        {
            var document = XDocument.Parse(responseContent);
            var faultString = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "faultstring")?.Value;
            return string.IsNullOrWhiteSpace(faultString) ? "Unknown SOAP fault." : faultString;
        }
        catch
        {
            return "Unknown SOAP fault.";
        }
    }
}