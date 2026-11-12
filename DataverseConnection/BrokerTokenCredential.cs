using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace DataverseConnection;

/// <summary>
/// Acquires a token for one configured Dataverse environment from a Unix-domain socket broker.
/// Uses one connection per request; does not cache, persist or log tokens and never falls back
/// to another credential. The caller (including ServiceClient) owns in-memory token caching.
/// </summary>
public sealed class BrokerTokenCredential : TokenCredential
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly UnixDomainSocketEndPoint _endpoint;
    private readonly string _scope;
    private readonly TimeSpan _timeout;
    private readonly int _maxResponseBytes;

    /// <summary>Creates a credential restricted to the HTTPS authority of dataverseUrl.</summary>
    /// <param name="dataverseUrl">Configured Dataverse environment URL; any path is ignored.</param>
    /// <param name="socketPath">Absolute filesystem path to a trusted Unix-domain socket.</param>
    /// <param name="options">Optional bounded transport limits, copied at construction.</param>
    public BrokerTokenCredential(
        string dataverseUrl,
        string socketPath,
        BrokerTokenCredentialOptions? options = null)
    {
        if (!Uri.TryCreate(dataverseUrl, UriKind.Absolute, out var environment)
            || environment.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(environment.Host)
            || !string.IsNullOrEmpty(environment.UserInfo)
            || !string.IsNullOrEmpty(environment.Query)
            || !string.IsNullOrEmpty(environment.Fragment))
            throw new ArgumentException("Broker authentication requires a valid HTTPS DataverseUrl.", nameof(dataverseUrl));

        if (string.IsNullOrWhiteSpace(socketPath) || !Path.IsPathFullyQualified(socketPath)
            || socketPath.Contains('\0'))
            throw new ArgumentException("DataverseTokenBrokerSocket must be an absolute Unix-domain socket path.", nameof(socketPath));

        options ??= new BrokerTokenCredentialOptions();
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options), "Broker timeout must be positive and at most five minutes.");
        if (options.MaxResponseBytes < 1 || options.MaxResponseBytes > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Broker response limit must be between 1 byte and 1 MiB.");

        _scope = $"{environment.GetLeftPart(UriPartial.Authority)}/.default";
        _endpoint = new UnixDomainSocketEndPoint(socketPath);
        _timeout = options.Timeout;
        _maxResponseBytes = options.MaxResponseBytes;
    }

    /// <inheritdoc />
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Exact match prevents alternate resources, multiple audiences or malformed scopes from
        // ever reaching the broker. This is the same authority-based scope ServiceClient uses.
        if (requestContext.Scopes is not { Length: 1 }
            || !string.Equals(requestContext.Scopes[0], _scope, StringComparison.Ordinal))
            throw new AuthenticationFailedException("Broker scope must match the configured Dataverse environment's /.default scope.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var ct = timeout.Token;
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(_endpoint, ct).ConfigureAwait(false);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            var request = StrictUtf8.GetBytes(JsonSerializer.Serialize(new { scope = _scope }) + "\n");
            await stream.WriteAsync(request.AsMemory(), ct).ConfigureAwait(false);

            // Never read or allocate more than the configured frame limit. LF is required even
            // if the peer closes; do not wait for EOF after a complete frame.
            var response = new byte[_maxResponseBytes];
            var used = 0;
            while (used < response.Length)
            {
                var read = await stream.ReadAsync(
                    response.AsMemory(used, Math.Min(4096, response.Length - used)), ct).ConfigureAwait(false);
                if (read == 0)
                    throw InvalidResponse();
                var newline = Array.IndexOf(response, (byte)'\n', used, read);
                used += read;
                if (newline >= 0)
                {
                    if (newline != used - 1)
                        throw InvalidResponse();
                    ct.ThrowIfCancellationRequested();
                    var token = ParseResponse(response.AsSpan(0, newline));
                    ct.ThrowIfCancellationRequested();
                    return token;
                }
            }
            throw new AuthenticationFailedException("Token broker response exceeded the configured size limit.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new AuthenticationFailedException("Token broker request timed out.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or PlatformNotSupportedException)
        {
            // Do not attach transport or parser exceptions: broker response/token data must
            // never appear in diagnostic messages, even when callers log the exception.
            throw new AuthenticationFailedException("Unable to acquire a token from the configured token broker.");
        }
    }

    private static AccessToken ParseResponse(ReadOnlySpan<byte> response)
    {
        try
        {
            using var document = JsonDocument.Parse(StrictUtf8.GetString(response));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw InvalidResponse();

            string? accessToken = null;
            string? expiry = null;
            DateTimeOffset expiresOn = default;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw InvalidResponse();
                switch (property.Name)
                {
                    case "accessToken" when accessToken is null:
                        accessToken = property.Value.GetString();
                        break;
                    case "expiresOn" when expiry is null:
                        expiry = property.Value.GetString();
                        if (!property.Value.TryGetDateTimeOffset(out expiresOn))
                            throw InvalidResponse();
                        break;
                    default:
                        throw InvalidResponse();
                }
            }

            // Require an explicit timezone, not a locale-dependent date or local wall-clock time.
            var hasTimeZone = expiry is not null && (expiry.EndsWith('Z')
                || (expiry.Length >= 6 && expiry[^3] == ':' && (expiry[^6] is '+' or '-')));
            if (string.IsNullOrWhiteSpace(accessToken)
                || accessToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
                || !hasTimeZone)
                throw InvalidResponse();
            if (expiresOn <= DateTimeOffset.UtcNow)
                throw new AuthenticationFailedException("Token broker returned an expired token.");

            return new AccessToken(accessToken, expiresOn);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or FormatException or InvalidOperationException)
        {
            throw InvalidResponse();
        }
    }

    private static AuthenticationFailedException InvalidResponse() =>
        new("Token broker returned a malformed response.");
}
