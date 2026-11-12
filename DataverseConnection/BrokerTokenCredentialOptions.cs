namespace DataverseConnection;

/// <summary>Transport limits for <see cref="BrokerTokenCredential"/>.</summary>
public sealed class BrokerTokenCredentialOptions
{
    /// <summary>
    /// Total timeout for connecting, writing and reading one request. Defaults to 30 seconds.
    /// Must be positive and no greater than five minutes; infinite timeouts are not allowed.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum UTF-8 response size in bytes, including the terminating newline.
    /// Defaults to 64 KiB. Must be between 1 and 1 MiB.
    /// </summary>
    public int MaxResponseBytes { get; set; } = 64 * 1024;
}
