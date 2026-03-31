# Salesforce Connected App Setup

## Purpose

This application supports Salesforce browser-based session login through OAuth 2.0 Authorization Code Flow with PKCE.

To use Session login, Salesforce must provide a Connected App and the application must be configured with its Client ID.

## Required Salesforce Setup

1. Open Salesforce Setup.
2. Go to App Manager.
3. Create a new Connected App.
4. Enable OAuth Settings.
5. Add a callback URL.

Recommended callback URL:

`http://localhost:17171/callback/`

If you choose a different port, use the same port in the application's `Callback Port` setting.

6. Add OAuth scopes.

Minimum recommended scopes:

- `Access and manage your data (api)`
- `Perform requests at any time (refresh_token, offline_access)`

7. Save the Connected App.
8. Copy the Consumer Key.

The Consumer Key is the value used as the application's `Client ID`.

## Application Configuration

In the application's configuration panel:

1. Set `Login Method` to `Session`.
2. Set `Login URL`.

Use one of these defaults unless your org requires a custom domain:

- Production: `https://login.salesforce.com`
- Sandbox: `https://test.salesforce.com`

If your organization requires My Domain or an SSO-specific domain, use that domain instead.

3. Set `Client ID` to the Connected App Consumer Key.
4. Set `Callback Port` to the port that matches the callback URL configured in Salesforce.

## Login Flow

1. Click `Login`.
2. The application opens the system browser.
3. Salesforce redirects the user to the Salesforce login page or to the organization's SSO provider.
4. After successful authentication, Salesforce redirects the browser back to the local callback URL.
5. The application exchanges the authorization code for tokens and stores the session securely using DPAPI.

## Notes For SSO Environments

If the Salesforce organization allows only SSO-based login, the application can still be used.

The browser-based Session login flow is the recommended path for SSO environments because the identity provider challenge is handled in the browser.

## Troubleshooting

### Redirect URI Mismatch

If Salesforce reports a redirect URI mismatch, ensure that the callback URL configured in the Connected App exactly matches the application's local callback URL.

### Browser Opens But Login Fails Immediately

Check the following:

- `Client ID` is correct.
- `Login URL` points to the correct Salesforce domain.
- The Connected App is approved for the user or profile.
- Required OAuth scopes are enabled.

### Session Login Works But API Calls Fail

Verify that the Connected App user has permission to access the target objects and query the requested data.