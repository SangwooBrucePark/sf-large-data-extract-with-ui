# User Guide: Salesforce Session Login Setup

## Purpose

This guide explains how to configure Salesforce and this application for browser-based session login.

The current application implementation uses OAuth 2.0 Authorization Code Flow with PKCE and a local loopback callback URL.

## Before You Start

Prepare the following:

- Access to Salesforce Setup
- Permission to create or manage the Salesforce app used for OAuth login
- The application running on your Windows machine

## Required Callback URL

The current application expects this callback URL format:

`http://localhost:17171/callback/`

Important:

- The callback URL in Salesforce must exactly match the value used by the application.
- `localhost` and `127.0.0.1` are not interchangeable for OAuth redirect URI matching.
- If you change the callback port in the application, update the Salesforce callback URL to the same port.

## Salesforce Setup

### 1. Open Salesforce Setup

1. Open Salesforce.
2. Go to `Setup`.
3. Open the app configuration page used for OAuth login.

### 2. Enable OAuth

Enable OAuth for the app.

### 3. Set the Callback URL

Use this value:

`http://localhost:17171/callback/`

### 4. Add OAuth Scopes

Add at least these scopes:

- `Access and manage your data (api)`
- `Perform requests at any time (refresh_token, offline_access)`

### 5. Enable the Correct Flow

Enable:

- `Enable Authorization Code and Credentials Flow`

Do not enable other flows unless your organization explicitly requires them.

### 6. Security Settings

Enable:

- `Require Proof Key for Code Exchange (PKCE)`

Disable:

- `Require secret for Web Server Flow`
- `Require secret for Refresh Token Flow`

Why this matters:

- The current application sends `client_id`, `redirect_uri`, `code`, and `code_verifier`.
- The current application does not send `client_secret` during token exchange.
- If Salesforce requires a secret, token exchange will fail with `invalid_client`.

### 7. Save the Salesforce App

After saving, copy the `Consumer Key`.

Important:

- Use the Salesforce `Consumer Key` as the application's `Client ID`.
- Do not use the `Consumer Secret` as the `Client ID`.

## Application Configuration

In the application configuration panel, set the following values:

- `Login Method` = `Session`
- `Client ID` = Salesforce `Consumer Key`
- `Callback Port` = `17171`
- `Login URL`:
  - Production: `https://login.salesforce.com`
  - Sandbox: `https://test.salesforce.com`

Important:

- The application currently has no field for `Consumer Secret`.
- If the Salesforce app requires a secret, login will not complete successfully.

## Login Steps

1. Start the application.
2. Set `Login Method` to `Session`.
3. Enter the Salesforce `Consumer Key` into `Client ID`.
4. Verify that `Callback Port` matches the Salesforce callback URL.
5. Click `Login`.
6. Sign in through the browser.
7. Return to the application after the browser redirects to the local callback URL.

## What Success Looks Like

If login succeeds:

- The browser returns to the local callback URL.
- The application stores the session locally.
- The application status view reports a successful login.

Session data is stored under:

`%LocalAppData%\LargeDataExportWithUI`

Files:

- `appsettings.json`
- `session.bin`

## Troubleshooting

### Redirect URI Mismatch

If Salesforce reports `redirect_uri_mismatch`, verify all of the following:

- The Salesforce callback URL is `http://localhost:17171/callback/`
- The application is also using `localhost`
- The port matches exactly
- The `/callback/` path is included
- The trailing slash is included

### Invalid Client

If the application reports `invalid_client` or `invalid client credentials`, check the following:

- `Client ID` contains the Salesforce `Consumer Key`
- `Consumer Secret` was not entered into `Client ID`
- `Require secret for Web Server Flow` is disabled
- `Require secret for Refresh Token Flow` is disabled
- `Enable Authorization Code and Credentials Flow` is enabled
- `Require Proof Key for Code Exchange (PKCE)` is enabled

### Browser Says Login Completed But The App Still Fails

This usually means the browser callback succeeded, but the token exchange failed afterward.

Most common causes:

- Incorrect `Client ID`
- Secret-required Salesforce configuration
- Callback mismatch between Salesforce and the application

### Callback Port Conflict

If the application cannot listen on the callback prefix:

1. Change `Callback Port` in the application.
2. Update the Salesforce callback URL to the same port.
3. Retry login.

## Notes For SSO Environments

Session login is suitable for SSO environments because the browser handles the identity provider challenge.

If your organization requires a custom Salesforce domain, use that domain in `Login URL` instead of the default production or sandbox URL.