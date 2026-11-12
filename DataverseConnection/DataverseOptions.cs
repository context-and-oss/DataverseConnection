using Azure.Core;
using Azure.Identity;

namespace DataverseConnection
{
    /// <summary>
    /// Selects the token credential used to authenticate with Dataverse.
    /// </summary>
    public enum DataverseCredentialType
    {
        /// <summary>
        /// Uses <see cref="AzureCliCredential"/> exclusively.
        /// </summary>
        AzureCliCredential = 1,

        /// <summary>
        /// Uses <see cref="DeviceCodeCredential"/> exclusively.
        /// </summary>
        DeviceCodeCredential = 2,

        /// <summary>
        /// Uses <see cref="InteractiveBrowserCredential"/> exclusively. This is the default.
        /// </summary>
        InteractiveBrowserCredential = 3,

        /// <summary>
        /// Uses <see cref="BrokerTokenCredential"/> exclusively, with no identity fallback.
        /// </summary>
        Broker = 4
    }

    /// <summary>
    /// Options for configuring a Dataverse connection.
    /// </summary>
    public class DataverseOptions
    {
        /// <summary>
        /// The Dataverse environment URL (for example, https://org.crm4.dynamics.com).
        /// </summary>
        public string DataverseUrl { get; set; } = string.Empty;

        /// <summary>
        /// The Azure credential type to use when <see cref="TokenCredential"/> is not set.
        /// The default is <see cref="DataverseCredentialType.InteractiveBrowserCredential"/>.
        /// </summary>
        public DataverseCredentialType CredentialType { get; set; } = DataverseCredentialType.InteractiveBrowserCredential;

        /// <summary>
        /// Absolute Unix-domain socket path for broker authentication. Read from
        /// IConfiguration's DataverseTokenBrokerSocket when using AddDataverse*.
        /// </summary>
        public string? TokenBrokerSocket { get; set; }

        /// <summary>
        /// Timeout and response-size limits used when CredentialType is Broker.
        /// </summary>
        public BrokerTokenCredentialOptions? BrokerTokenCredentialOptions { get; set; }

        /// <summary>
        /// An explicitly provided Azure credential. When set, this takes precedence over
        /// <see cref="CredentialType"/> and all credential-specific options. Use this to plug in
        /// any credential (for example, <see cref="DefaultAzureCredential"/> or a service principal)
        /// when calling the library directly.
        /// </summary>
        public TokenCredential? TokenCredential { get; set; }

        /// <summary>
        /// Options used when <see cref="CredentialType"/> is
        /// <see cref="DataverseCredentialType.AzureCliCredential"/>.
        /// </summary>
        public AzureCliCredentialOptions? AzureCliCredentialOptions { get; set; }

        /// <summary>
        /// Options used when <see cref="CredentialType"/> is
        /// <see cref="DataverseCredentialType.DeviceCodeCredential"/>.
        /// </summary>
        public DeviceCodeCredentialOptions? DeviceCodeCredentialOptions { get; set; }

        /// <summary>
        /// Options used when <see cref="CredentialType"/> is
        /// <see cref="DataverseCredentialType.InteractiveBrowserCredential"/>.
        /// </summary>
        public InteractiveBrowserCredentialOptions? InteractiveBrowserCredentialOptions { get; set; }
    }
}
