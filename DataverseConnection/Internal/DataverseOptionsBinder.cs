using System;
using Microsoft.Extensions.Configuration;

namespace DataverseConnection.Internal
{
    /// <summary>
    /// Applies the library's default "translation" from application settings onto a
    /// <see cref="DataverseOptions"/> instance. Only fills values the caller has not already
    /// set, so explicit properties (or a <c>configureOptions</c> override) always win.
    /// </summary>
    internal static class DataverseOptionsBinder
    {
        internal const string DataverseUrlKey = "DataverseUrl";
        internal const string LegacyDataverseUrlKey = "DATAVERSE_URL";
        internal const string DataverseCredentialTypeKey = "DataverseCredentialType";
        internal const string DataverseTokenBrokerSocketKey = "DataverseTokenBrokerSocket";
        internal const string LegacyDataverseCredentialTypeKey = "DATAVERSE_CREDENTIAL_TYPE";

        /// <summary>
        /// Reads the flat keys <c>DataverseUrl</c>, <c>DataverseTokenBrokerSocket</c> and
        /// <c>DataverseCredentialType</c>, and applies them to <paramref name="options"/>.
        /// The URL and credential type also support their legacy uppercase forms.
        /// </summary>
        public static void Bind(DataverseOptions options, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(configuration);

            if (string.IsNullOrWhiteSpace(options.DataverseUrl))
            {
                var url = GetDataverseUrl(configuration);
                if (!string.IsNullOrWhiteSpace(url))
                    options.DataverseUrl = url;
            }

            if (string.IsNullOrWhiteSpace(options.TokenBrokerSocket))
                options.TokenBrokerSocket = configuration[DataverseTokenBrokerSocketKey];

            var credentialType = GetFirstConfiguredValue(
                configuration,
                DataverseCredentialTypeKey,
                LegacyDataverseCredentialTypeKey);
            if (!string.IsNullOrWhiteSpace(credentialType))
            {
                options.CredentialType = credentialType.Trim().ToLowerInvariant() switch
                {
                    "browser" => DataverseCredentialType.InteractiveBrowserCredential,
                    "devicecode" => DataverseCredentialType.DeviceCodeCredential,
                    "azcli" => DataverseCredentialType.AzureCliCredential,
                    "broker" => DataverseCredentialType.Broker,
                    _ => throw new ArgumentException(
                        $"Unknown {DataverseCredentialTypeKey} '{credentialType}'. Valid values: browser, devicecode, azcli, broker.")
                };
            }
        }

        internal static string? GetDataverseUrl(IConfiguration configuration) =>
            GetFirstConfiguredValue(configuration, DataverseUrlKey, LegacyDataverseUrlKey);

        private static string? GetFirstConfiguredValue(
            IConfiguration configuration,
            string pascalCaseKey,
            string legacyUppercaseKey)
        {
            var value = configuration[pascalCaseKey];
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : configuration[legacyUppercaseKey];
        }
    }
}
