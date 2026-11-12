# DataverseConnection (.NET NuGet Package)

## Overview

**DataverseConnection** is a .NET 10 class library that provides reusable, dependency-injectable connection logic for Microsoft Dataverse. It is opinionated about interactive authentication: when used from the CLI you choose between interactive credentials and a headless token broker, while library callers can still plug in any `TokenCredential`.

The included `DataverseWhoAmI` console application demonstrates the library and verifies connectivity.

## Features

- Reusable .NET 10 library for Dataverse connectivity
- Dependency injection through `AddDataverse`, `AddDataverseWithOrganizationServices`, and `AddDataverseFactory`
- Four built-in token credential types:
  - `InteractiveBrowserCredential` (**default**)
  - `DeviceCodeCredential`
  - `AzureCliCredential`
  - `BrokerTokenCredential` (headless Unix-domain socket broker; no app secret in the tool)
- Any explicitly supplied `TokenCredential` when calling the library directly (for example `DefaultAzureCredential` or a service principal)
- Credential-specific Azure Identity options
- Persistent token caching by default for the interactive credentials, so you log in as rarely as possible
- Token caching isolated by credential instance and Dataverse resource
- Cross-platform support for Windows, Linux, and macOS

## Prerequisites

- .NET 10 SDK
- Access to a Microsoft Dataverse environment
- An Azure identity with access to that environment

## Basic registration

```csharp
using DataverseConnection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddDataverse(options =>
{
    // Optional when DataverseUrl is available through IConfiguration.
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
});
```

If no credential type or custom credential is specified, the library uses `InteractiveBrowserCredential` — a person running the tool from a computer can always complete a browser sign-in.

> **Tip:** You usually don't need to configure anything in code. If you register an `IConfiguration`, the library reads the Dataverse URL and credential type from your app settings automatically — see [Configuration](#configuration). Use the `configureOptions` callback only when a tool needs something specific.

## Selecting a credential type

Set `DataverseOptions.CredentialType` to one of the four built-in credentials. Selecting a type does not create a fallback chain.

| Value | Credential | Notes |
| --- | --- | --- |
| `InteractiveBrowserCredential` (default) | `InteractiveBrowserCredential` | Opens a browser sign-in; persistent token cache by default. |
| `DeviceCodeCredential` | `DeviceCodeCredential` | Prints a code to sign in from any device; persistent token cache by default. |
| `AzureCliCredential` | `AzureCliCredential` | Reuses an existing `az login` session (the `az` CLI owns its own cache). |
| `Broker` | `BrokerTokenCredential` | Gets an environment-scoped token from a trusted Unix-domain socket; no identity fallback or persistent token cache. |

### Azure CLI only

This requires an authenticated Azure CLI session, normally created with `az login`.

```csharp
services.AddDataverse(options =>
{
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
    options.CredentialType = DataverseCredentialType.AzureCliCredential;
});
```

You can provide Azure CLI-specific options:

```csharp
using Azure.Identity;

services.AddDataverse(options =>
{
    options.CredentialType = DataverseCredentialType.AzureCliCredential;
    options.AzureCliCredentialOptions = new AzureCliCredentialOptions
    {
        TenantId = "<tenant-id>"
    };
});
```

### Device code only

```csharp
using Azure.Identity;

services.AddDataverse(options =>
{
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
    options.CredentialType = DataverseCredentialType.DeviceCodeCredential;
    options.DeviceCodeCredentialOptions = new DeviceCodeCredentialOptions
    {
        TenantId = "<tenant-id>",
        ClientId = "<application-client-id>"
    };
});
```

The device-code credential presents instructions that let the user authenticate from a browser, including on a different device.

### Interactive browser only

```csharp
using Azure.Identity;

services.AddDataverse(options =>
{
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
    options.CredentialType = DataverseCredentialType.InteractiveBrowserCredential;
    options.InteractiveBrowserCredentialOptions = new InteractiveBrowserCredentialOptions
    {
        TenantId = "<tenant-id>",
        ClientId = "<application-client-id>"
    };
});
```

When you do **not** supply `InteractiveBrowserCredentialOptions`, the library enables persistent token caching automatically (see [Persistent token caching](#persistent-token-caching)). Supplying your own options means the library uses them as-is and does not add caching on your behalf.

## Headless token-broker authentication

Existing tools using `AddDataverse`, `AddDataverseWithOrganizationServices`, or
`AddDataverseFactory` can opt in through their registered `IConfiguration` alone:

```json
{
  "DataverseUrl": "https://yourorg.crm4.dynamics.com",
  "DataverseCredentialType": "broker",
  "DataverseTokenBrokerSocket": "/run/task-runtime/dataverse.sock"
}
```

The task runtime implements the broker separately and keeps any app-registration
client secret outside the tool. The tool receives only a short-lived access token.
The broker identity still needs access to the configured Dataverse environment.

Equivalent programmatic options (including optional transport limits):

```csharp
services.AddDataverse(options =>
{
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
    options.CredentialType = DataverseCredentialType.Broker;
    options.TokenBrokerSocket = "/run/task-runtime/dataverse.sock";
    options.BrokerTokenCredentialOptions = new BrokerTokenCredentialOptions
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseBytes = 64 * 1024
    };
});
```

You can also instantiate `new BrokerTokenCredential(dataverseUrl, socketPath,
options)` and use its synchronous `GetToken` or asynchronous `GetTokenAsync`.
Both accept cancellation. Options are snapshotted when the credential is constructed.
The total connect/write/read timeout defaults to **30 seconds**; it must be positive
and at most **five minutes** (no infinite timeout). The response limit defaults to
**64 KiB including the newline**, with a supported range of **1 byte to 1 MiB**.
Broker mode requires a valid HTTPS environment URL and an absolute socket path.

Broker failures are fail-closed: missing/inaccessible sockets, timeout, protocol
errors, invalid scopes and expired tokens fail authentication. No browser, device
code, Azure CLI, managed identity or other credential is attempted. An explicitly
supplied `DataverseOptions.TokenCredential` still takes precedence over the selected
mode and all broker settings, as it does for the other modes.

The broker credential does not persist or log tokens and does not use the
interactive credential cache. It plugs into the existing ServiceClient token-provider
and in-memory cache/refresh flow, isolated by credential instance and Dataverse
resource. The task-runtime broker must likewise avoid logging response payloads or
tokens.

### Task-runtime broker protocol (Unix-domain socket)

This is the exact wire contract for the separately implemented runtime broker:

1. Listen on the configured Unix-domain **stream** socket (not TCP or an HTTP endpoint).
   The tool opens **one new connection per token request**; no connection is reused.
2. Messages are **UTF-8 without a BOM**, newline-delimited JSON. Each message is one
   JSON object on one line, terminated by a single LF byte (`0x0A`). No length prefix,
   HTTP headers, handshake or additional messages are used. Writes may be fragmented;
   peers must read until the newline, within their limits.
3. The tool sends exactly one request, with this property:

   ```json
   {"scope":"https://<configured-dataverse-host>/.default"}
   ```

   For example, `DataverseUrl=https://yourorg.crm4.dynamics.com/some/path` produces
   `{"scope":"https://yourorg.crm4.dynamics.com/.default"}` followed by LF. The scope
   uses the normalized HTTPS authority (including a non-default port if configured);
   paths are ignored. URLs containing user information, query strings or fragments
   are rejected. The credential only accepts exactly one scope matching that
   environment's `/.default` scope, before opening the socket.
4. Return exactly one response, with these two case-sensitive string properties:

   ```json
   {"accessToken":"...","expiresOn":"<ISO-8601 timestamp>"}
   ```

   A concrete response example (also followed by LF):

   ```json
   {"accessToken":"<bearer-access-token>","expiresOn":"2099-01-01T00:00:00Z"}
   ```

   `expiresOn` must be an ISO-8601 date/time with an explicit UTC `Z` or numeric
   offset (`+HH:mm` / `-HH:mm`), optionally with fractional seconds. It must be in the
   future when received. `accessToken` must be nonempty and contain no whitespace or
   control characters. Missing, duplicate or additional properties, wrong types,
   invalid UTF-8/JSON, missing LF, oversized responses and expired tokens are rejected.
   There is no error-response schema: on failure, close the connection without a
   successful response; the tool fails authentication. Do not return diagnostic
   text in place of a token or append it to the response.
5. The tool reads the complete response line, then closes the connection; it does
   **not** require the broker to close first. The broker must tolerate disconnects
   caused by cancellation, timeout or response-size enforcement. Timeouts cover the
   whole exchange, not each individual read, so trickling bytes does not extend them.

**Runtime security boundary:** Bind the socket in a private, runtime-owned directory
and restrict socket/directory permissions to authorized task identities. The broker
must authorize the caller and independently allowlist the permitted Dataverse scope;
client-side scope validation does not authenticate a caller to the broker. Do not
mount or expose app-registration secrets or interactive token caches into the task.
Only expose the trusted socket. The library does not create the broker, provision
identities, verify JWT claims/signatures, or secure the socket's filesystem permissions;
it trusts the authorized runtime broker to return a token for the requested audience.

## Providing a custom TokenCredential

The four built-in types are the opinionated choices for tools. When calling the library directly you are not limited to them: set `DataverseOptions.TokenCredential` to any credential — for example `DefaultAzureCredential`, a service principal, or a managed identity. An explicitly supplied `TokenCredential` always takes precedence over `CredentialType` and all credential-specific options.

```csharp
using Azure.Identity;

// Use DefaultAzureCredential (or any TokenCredential) when hosting the library yourself.
TokenCredential credential = new DefaultAzureCredential();

services.AddDataverse(options =>
{
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
    options.TokenCredential = credential;
});
```

## Resolving and using ServiceClient

```csharp
using Microsoft.PowerPlatform.Dataverse.Client;

using var provider = services.BuildServiceProvider();
var serviceClient = provider.GetRequiredService<ServiceClient>();
```

To register the related organization-service interfaces as well:

```csharp
services.AddDataverseWithOrganizationServices(options =>
{
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
    options.CredentialType = DataverseCredentialType.AzureCliCredential;
});
```

This registers:

- `ServiceClient`
- `IOrganizationServiceAsync2`
- `IOrganizationServiceAsync`
- `IOrganizationService`

## ServiceClientFactory

Use `IServiceClientFactory` when you need separate `ServiceClient` instances:

```csharp
services.AddDataverseFactory(options =>
{
    options.DataverseUrl = "https://yourorg.crm4.dynamics.com";
    options.CredentialType = DataverseCredentialType.DeviceCodeCredential;
});

using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<IServiceClientFactory>();

var defaultClient = factory.CreateClient();

var interactiveClient = factory.CreateClient(new DataverseOptions
{
    DataverseUrl = "https://anotherorg.crm4.dynamics.com",
    CredentialType = DataverseCredentialType.InteractiveBrowserCredential
});
```

To inject a custom credential into the factory, set `DataverseOptions.TokenCredential` in the `configureOptions` callback (or per client via `CreateClient`). A per-client `DataverseOptions.TokenCredential` always has the highest precedence.

## Persistent token caching

For `InteractiveBrowserCredential` and `DeviceCodeCredential`, the library enables persistent token caching by default (when you do not pass your own credential-specific options). The cache and signed-in account are indexed by the normalized Dataverse environment URL. Separate projects that use the same environment reuse its sign-in, while a different environment gets an independent sign-in and cannot overwrite the first one. `AzureCliCredential` is unaffected because the `az` CLI manages its own cache.

The cache uses the operating system keychain when one is available (DPAPI on Windows, Keychain on macOS, and **libsecret on Linux/WSL**). Because containers and other headless Linux environments commonly have no `libsecret`, Linux also permits Azure Identity's unencrypted file-based fallback. Windows and macOS continue to require encrypted storage.

> **Linux/container security:** The Linux fallback contains reusable authentication tokens and must be treated as a secret. Run the container as a dedicated non-root user, do not share its home directory, and restrict any mounted cache volume to that user. To keep the login across container replacements, persist the user's home-directory cache data (including `~/.IdentityService` and `~/.dataverseconnection`) in a private volume.

## Configuration

By default the library reads its settings from the registered `IConfiguration`, so a tool does **not** have to write any authentication code — it just registers an `IConfiguration` and calls one of the `AddDataverse*` methods. Every tool can share the same app settings and behave consistently without reinventing the wiring.

Register a configuration source and the Dataverse services:

```csharp
using DataverseConnection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);

// URL and credential type are read from configuration automatically.
services.AddDataverseWithOrganizationServices();
services.AddDataverseFactory();
```

The library reads these flat PascalCase keys (from `appsettings.json`, environment variables, or any other configuration source). The legacy uppercase keys remain supported for backward compatibility:

| Key | Required | Values |
| --- | --- | --- |
| `DataverseUrl` (or legacy `DATAVERSE_URL`) | Yes (unless set on `DataverseOptions.DataverseUrl`) | The environment URL, e.g. `https://yourorg.crm4.dynamics.com`. |
| `DataverseCredentialType` (or legacy `DATAVERSE_CREDENTIAL_TYPE`) | No (defaults to `browser`) | `browser`, `devicecode`, `azcli`, or `broker` (case-insensitive). |
| `DataverseTokenBrokerSocket` | Required for `broker` (unless set on `DataverseOptions.TokenBrokerSocket`) | Absolute Unix-domain socket path, e.g. `/run/task-runtime/dataverse.sock`. |

```json
{
  "DataverseUrl": "https://yourorg.crm4.dynamics.com",
  "DataverseCredentialType": "browser"
}
```

The credential-type strings map to the [opinionated credential types](#selecting-a-credential-type):

| Config value | Credential type |
| --- | --- |
| `browser` (default) | `InteractiveBrowserCredential` |
| `devicecode` | `DeviceCodeCredential` |
| `azcli` | `AzureCliCredential` |
| `broker` | `BrokerTokenCredential` (`DataverseCredentialType.Broker`) |

An unrecognized `DataverseCredentialType` (or legacy `DATAVERSE_CREDENTIAL_TYPE`) throws at startup, listing the valid values.

### Overriding the defaults

Values read from configuration are just the defaults. To do something specific — a fixed credential type, a custom `TokenCredential`, credential-specific options, or a hard-coded URL — pass a `configureOptions` callback. It runs **after** the configuration is applied, so anything you set there wins:

```csharp
services.AddDataverseWithOrganizationServices(options =>
{
    // Overrides DataverseCredentialType from configuration.
    options.CredentialType = DataverseCredentialType.AzureCliCredential;
});
```

Because the callback overrides configuration, a tool that needs full control writes only the lines it cares about; everything else still comes from the shared app settings.

## WhoAmI verification tool

```powershell
cd DataverseWhoAmI
dotnet run
```

The tool executes `WhoAmIRequest` and prints the user, business unit, and organization IDs.

## License

MIT
