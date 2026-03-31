# Large Data Export For Salesforce

A Windows desktop application for extracting large-scale data from Salesforce orgs.
It solves the Salesforce SOQL `IN` clause size limit by splitting a large list of values into multiple batches and executing them automatically, one at a time, until the full keyword set has been processed.

https://github.com/user-attachments/assets/211f4ace-604c-4319-9f99-9c90507efd24

---

## Overview

Salesforce SOQL has practical query length limits and `IN` clause size constraints that prevent placing thousands of values into a single query.

This application accepts a SOQL template with a replacement token and a separate list of keyword values. It converts the values into SOQL-safe literals, splits them into limit-safe batches, executes the query once per batch, and merges all results into a single CSV file.

The default extraction path is **Bulk API 2.0**. Alternative methods (`REST Query`, `REST QueryAll`) can be selected through the configuration panel when needed.

---

## Key Features

- SOQL template editor with a configurable `IN` clause token
- Keyword input by paste or file load
- Automatic batch splitting based on a configurable chunk size (default: 500)
- Three extraction strategies: Bulk API 2.0, REST Query, REST QueryAll
- Two authentication modes: Username/Password and browser-based Session (OAuth 2.0 PKCE)
- PropertyGrid-based configuration with automatic save
- Colorized status log with timestamps
- CSV output via Save File dialog
- Self-contained, single-file Windows executable — no runtime installation required

---

## Requirements

| Requirement       | Details                         |
|-------------------|---------------------------------|
| Operating System  | Windows (x64)                   |
| Salesforce Access | API-enabled org and user        |
| .NET runtime      | Not required — bundled in the executable |

---

## Installation

No installer is needed.

1. Download the latest release executable from the link below.

   **[Download latest release](https://github.com/SangwooBrucePark/sf-large-data-extract-with-ui/releases/latest)**

2. Place it in any directory on your Windows machine.
3. Double-click to run.

Application data is stored in `%LocalAppData%\LargeDataExportWithUI\` and does not require write access to the executable directory.

---

## Getting Started

### 1. Configure Authentication

Open the **Configurations** panel on the right side of the application.

**Option A — Password Login**

Set `Login Method` to `Password` and fill in:

- `Org Type` — `Production` or `Sandbox`
- `Login URL` — auto-populated from org type; override only if your org uses My Domain or a custom SSO domain
- `Username`, `Password`, `Security Token` — your Salesforce credentials

> If your org IP restrictions do not require a security token, leave the `Security Token` field blank.

**Option B — Session Login (OAuth 2.0 PKCE)**

Set `Login Method` to `Session` and fill in:

- `Login URL` — your org login or SSO URL
- `Client ID` — Consumer Key from your Salesforce Connected App

Click **Login** to open the browser-based authentication flow. The application will exchange the authorization code for tokens and store them securely.

See [Salesforce Connected App Setup](docs/salesforce-connected-app-instructions.md) for how to create a Connected App, and [Session Login User Guide](docs/user-guide-salesforce-session-login.md) for detailed PKCE configuration steps.

### 2. Enter the SOQL Template

Type or paste your SOQL query into the **SOQL** editor. Use the token configured in `IN Clause Token` (default: `__IN_CLAUSE__`) as a placeholder where the `IN` clause values should be inserted.

Example:

```soql
SELECT Id, Name, Email FROM Contact WHERE Id IN (__IN_CLAUSE__)
```

### 3. Provide Keywords

Use one of two input modes:

**Paste mode** — Type or paste keyword values directly into the **Keywords** editor. Use the configured `Delimiter` (default: `,`) to separate values.

**File mode** — Set `Keywords Source` in the configuration panel to a file path. The application will read keyword values from the file using the configured `Delimiter`.

Optional settings:
- `Skip First Row` — skip the header row when loading a file
- `Keyword Value Type` — `Text` (wraps values in single quotes and escapes special characters) or `Number` (inserts values unquoted)

### 4. Run the Extraction

Click **Run**.

If no output file has been selected yet, a Save File dialog will appear. Choose a directory and file name; the default name is `extract_YYYYMMDD_HHmmss.csv`.

The application will:

1. Validate configuration and inputs.
2. Authenticate with Salesforce (Password mode) or use the stored session token (Session mode).
3. Split the keyword list into batches of the configured `Chunk Size`.
4. Execute the SOQL query once per batch using the selected `Extraction Method`.
5. Append each batch result to the output CSV file.
6. Report progress, record counts, and any failures in the **Status View**.

---

## Configuration Reference

All settings are accessible through the **Configurations** panel. Changes are saved automatically.

### Authentication

| Setting           | Description |
|-------------------|-------------|
| Login Method      | `Password` — credential-based login. `Session` — browser-based OAuth 2.0 PKCE login. |
| Org Type          | `Production` or `Sandbox`. Sets the default Login URL. |
| Login URL         | Salesforce login endpoint. Defaults to `https://login.salesforce.com` or `https://test.salesforce.com`. Override for My Domain or SSO URLs. |
| Credential Source | `Custom` — enter values directly. `Env Var` — enter environment variable names to resolve at runtime. Applies to all credential fields at once. |
| Username          | Salesforce username or environment variable name. |
| Password          | Salesforce password or environment variable name. |
| Security Token    | Salesforce security token or environment variable name. Leave blank if not required by your org. |
| Client ID         | Consumer Key of the Salesforce Connected App. Required for Session login. |
| Callback Port     | Local HTTP port for the OAuth loopback redirect. Default: `17171`. Must match the Connected App callback URL. |

### Query Input

| Setting          | Description |
|------------------|-------------|
| IN Clause Token  | Placeholder string in the SOQL template that will be replaced with the batched `IN` clause values. Default: `__IN_CLAUSE__`. |
| Keywords Source  | Optional file path. When set, keywords are read from the file. When empty, the Keywords editor is used. |
| Delimiter        | Separator between keyword values. Default: `,`. Use `\n` or `\r\n` for line-separated files. |
| Skip First Row   | Skip the first row of keyword input. Useful when loading a file with a header row. |
| Keyword Value Type | `Text` — values are wrapped in single quotes and SOQL-escaped. `Number` — values are inserted unquoted. |

### Execution

| Setting           | Description |
|-------------------|-------------|
| Extraction Method | API strategy used for each batch query. See [Extraction Methods](#extraction-methods). Default: `Bulk API 2.0`. |
| Chunk Size        | Number of keyword values per batch. Default: `500`. Reduce this if queries are too long or jobs time out. |

---

## Extraction Methods

| Method      | When to use |
|-------------|-------------|
| Bulk API 2.0 | Default. Best for large-scale extraction. Asynchronous job model, high throughput, suitable for millions of records. |
| REST Query  | Direct synchronous query. Use for smaller runs, debugging, or when Bulk API is not appropriate. |
| REST QueryAll | Like REST Query but also returns soft-deleted and archived records. Use only when archived data is needed. |

---

## Authentication Modes

### Password Mode

Authentication is performed at execution time using the configured credentials.

- `Login Method` must be set to `Password`.
- The **Login** button is disabled in this mode.
- `Client ID` is required. It is used as the OAuth client during password-based token exchange.
- `Username`, `Password`, and `Security Token` must be filled in (or set to environment variable names if `Credential Source` is `Env Var`).

### Session Mode (OAuth 2.0 PKCE)

Authentication is performed interactively through the system browser.

- `Login Method` must be set to `Session`.
- A Salesforce Connected App must be configured with the OAuth callback URL and appropriate scopes.
- Click **Login** to start the browser-based flow.
- After successful authentication, the session token is stored encrypted using Windows DPAPI.
- The session is reused on subsequent runs until the token expires.

The required Connected App configuration is described in [docs/salesforce-connected-app-instructions.md](docs/salesforce-connected-app-instructions.md).

---

## File Storage

| File | Location | Content |
|------|----------|---------|
| Configuration | `%LocalAppData%\LargeDataExportWithUI\appsettings.json` | JSON, plain text |
| Session token | `%LocalAppData%\LargeDataExportWithUI\session.bin` | Encrypted with Windows DPAPI (current user scope) |
| Output CSV | User-selected path via Save File dialog | Plain CSV |

---

## Building From Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- Windows x64

### Build (Debug)

```powershell
dotnet build LargeDataExportWithUI.App\LargeDataExportWithUI.App.csproj
```

### Publish (self-contained, single file)

```powershell
dotnet publish LargeDataExportWithUI.App\LargeDataExportWithUI.App.csproj -c Release -r win-x64 --self-contained true
```

Output is written to:

```
LargeDataExportWithUI.App\bin\Release\net10.0-windows\win-x64\publish\
```

The published executable is named `sf-largedataexport-standalone-v<version>.exe` and can be copied to any Windows x64 machine without installing .NET.

---

## Troubleshooting

### Authentication Fails (Password Mode)

- Verify `Username`, `Password`, and `Security Token` are correct.
- If the org does not enforce IP restrictions, try clearing `Security Token`.
- Confirm `Login URL` matches the org type (`login.salesforce.com` for production, `test.salesforce.com` for sandbox).
- If `Credential Source` is `Env Var`, confirm the named environment variables are defined and accessible in the session running the application.
- Verify `Client ID` is set to the Consumer Key of a Connected App in the org.

### Session Login Fails (OAuth/PKCE Mode)

- Confirm `Client ID` matches the Connected App Consumer Key exactly.
- Confirm the Connected App callback URL is `http://localhost:<CallbackPort>/callback/` and matches the application's `Callback Port` setting.
- Confirm that `Require Proof Key for Code Exchange (PKCE)` is enabled and `Require secret for Web Server Flow` is disabled in the Connected App.
- If the browser redirects but no token is received, check that the `Callback Port` is not blocked by a firewall or another process.

See [docs/user-guide-salesforce-session-login.md](docs/user-guide-salesforce-session-login.md) for full PKCE setup instructions.

### Queries Return No Records

- Verify the SOQL template uses the exact `IN Clause Token` string configured in settings (default: `__IN_CLAUSE__`).
- Confirm `Keyword Value Type` is correct — use `Text` for string fields and `Number` for numeric fields.
- Check the Status View for any API-level error messages returned from Salesforce.

### Export Stops Part Way Through

- The Status View reports per-batch progress, including any batch-level failures.
- Partial-batch failures do not cancel the remaining batches. Review the Status View to determine which batches failed.
- Reduce `Chunk Size` if individual batches are hitting query length or timeout limits.

---

## Notes

- Credentials entered as plain text in the configuration panel are stored in `appsettings.json` as plain text. For sensitive environments, use `Credential Source = Env Var` and supply values through environment variables.
- The session token stored in `session.bin` is encrypted with DPAPI and is only accessible to the Windows user account that created it.
- Application logs are visible only in the Status View and are not written to disk.
