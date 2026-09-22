# DataverseConnection (.NET NuGet Package)

## Overview

**DataverseConnection** is a .NET 10 class library that provides reusable, dependency-injectable connection logic for Microsoft Dataverse. It is opinionated about interactive authentication: when used from the CLI you choose between three human-friendly credential types, while library callers can still plug in any `TokenCredential`.

The included `DataverseWhoAmI` console application demonstrates the library and verifies connectivity.

## Features

- Reusable .NET 10 library for Dataverse connectivity
- Dependency injection through `AddDataverse`, `AddDataverseWithOrganizationServices`, and `AddDataverseFactory`
- Three opinionated Azure Identity credential types:
  - `InteractiveBrowserCredential` (**default**)
  - `DeviceCodeCredential`
  - `AzureCliCredential`
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

Set `DataverseOptions.CredentialType` to one of the three opinionated Azure Identity credentials. Selecting a type does not create a fallback chain.

| Value | Credential | Notes |
| --- | --- | --- |
| `InteractiveBrowserCredential` (default) | `InteractiveBrowserCredential` | Opens a browser sign-in; persistent token cache by default. |
| `DeviceCodeCredential` | `DeviceCodeCredential` | Prints a code to sign in from any device; persistent token cache by default. |
| `AzureCliCredential` | `AzureCliCredential` | Reuses an existing `az login` session (the `az` CLI owns its own cache). |

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

## Providing a custom TokenCredential

The three built-in types are the opinionated choices for the CLI. When calling the library directly you are not limited to them: set `DataverseOptions.TokenCredential` to any credential — for example `DefaultAzureCredential`, a service principal, or a managed identity. An explicitly supplied `TokenCredential` always takes precedence over `CredentialType` and all credential-specific options.

```csharp
using Azure.Core;
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

Use `IServiceClientFactory` when you need separate `ServiceClient` instances. Register an `IConfiguration` before resolving the factory, even when you supply the URL through options. An empty configuration is sufficient for this example; applications can use their existing configuration instead (see [Configuration](#configuration)).

```csharp
using DataverseConnection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

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

The library reads two flat PascalCase keys (from `appsettings.json`, environment variables, or any other configuration source). The legacy uppercase keys remain supported for backward compatibility:

| Key | Required | Values |
| --- | --- | --- |
| `DataverseUrl` (or legacy `DATAVERSE_URL`) | Yes (unless set on `DataverseOptions.DataverseUrl`) | The environment URL, e.g. `https://yourorg.crm4.dynamics.com`. |
| `DataverseCredentialType` (or legacy `DATAVERSE_CREDENTIAL_TYPE`) | No (defaults to `browser`) | `browser`, `devicecode`, or `azcli` (case-insensitive). |

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

An unrecognized `DataverseCredentialType` (or legacy `DATAVERSE_CREDENTIAL_TYPE`) throws when the registered `ServiceClient` or `IServiceClientFactory` is first resolved, listing the valid values. Configuration is validated before the `configureOptions` callback runs, so the callback cannot override an invalid configured credential type.

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
