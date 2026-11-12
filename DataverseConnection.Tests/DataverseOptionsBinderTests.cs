using DataverseConnection.Internal;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DataverseConnection.Tests;

public class DataverseOptionsBinderTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
            dict[key] = value;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Theory]
    [InlineData("DataverseUrl")]
    [InlineData("DATAVERSE_URL")]
    public void Bind_ReadsUrlFromConfiguration(string key)
    {
        var options = new DataverseOptions();

        DataverseOptionsBinder.Bind(options, Config((key, "https://org.crm4.dynamics.com")));

        Assert.Equal("https://org.crm4.dynamics.com", options.DataverseUrl);
    }

    [Theory]
    [InlineData("DataverseCredentialType", "azcli", DataverseCredentialType.AzureCliCredential)]
    [InlineData("DataverseCredentialType", "devicecode", DataverseCredentialType.DeviceCodeCredential)]
    [InlineData("DataverseCredentialType", "browser", DataverseCredentialType.InteractiveBrowserCredential)]
    [InlineData("DataverseCredentialType", "broker", DataverseCredentialType.Broker)]
    [InlineData("DataverseCredentialType", "  BROKER  ", DataverseCredentialType.Broker)]
    [InlineData("DATAVERSE_CREDENTIAL_TYPE", "broker", DataverseCredentialType.Broker)]
    [InlineData("DataverseCredentialType", "AZCLI", DataverseCredentialType.AzureCliCredential)]
    [InlineData("DataverseCredentialType", "  DeviceCode  ", DataverseCredentialType.DeviceCodeCredential)]
    [InlineData("DATAVERSE_CREDENTIAL_TYPE", "azcli", DataverseCredentialType.AzureCliCredential)]
    [InlineData("DATAVERSE_CREDENTIAL_TYPE", "devicecode", DataverseCredentialType.DeviceCodeCredential)]
    [InlineData("DATAVERSE_CREDENTIAL_TYPE", "browser", DataverseCredentialType.InteractiveBrowserCredential)]
    public void Bind_ParsesCredentialType_CaseInsensitively(
        string key,
        string configured,
        DataverseCredentialType expected)
    {
        var options = new DataverseOptions();

        DataverseOptionsBinder.Bind(options, Config((key, configured)));

        Assert.Equal(expected, options.CredentialType);
    }

    [Fact]
    public void Bind_PrefersPascalCaseKeys_WhenBothFormsArePresent()
    {
        var options = new DataverseOptions();

        DataverseOptionsBinder.Bind(options, Config(
            ("DataverseUrl", "https://pascal.crm4.dynamics.com"),
            ("DATAVERSE_URL", "https://uppercase.crm4.dynamics.com"),
            ("DataverseCredentialType", "azcli"),
            ("DATAVERSE_CREDENTIAL_TYPE", "devicecode")));

        Assert.Equal("https://pascal.crm4.dynamics.com", options.DataverseUrl);
        Assert.Equal(DataverseCredentialType.AzureCliCredential, options.CredentialType);
    }

    [Fact]
    public void Bind_DoesNotOverwriteExplicitUrl()
    {
        var options = new DataverseOptions { DataverseUrl = "https://explicit.crm4.dynamics.com" };

        DataverseOptionsBinder.Bind(options, Config(("DataverseUrl", "https://config.crm4.dynamics.com")));

        Assert.Equal("https://explicit.crm4.dynamics.com", options.DataverseUrl);
    }

    [Fact]
    public void Bind_ReadsBrokerSocket_FromConfiguration()
    {
        var options = new DataverseOptions();
        DataverseOptionsBinder.Bind(options, Config(
            ("DataverseCredentialType", "broker"),
            ("DataverseTokenBrokerSocket", "/tmp/configured.sock")));

        Assert.Equal(DataverseCredentialType.Broker, options.CredentialType);
        Assert.Equal("/tmp/configured.sock", options.TokenBrokerSocket);
    }

    [Fact]
    public void Bind_DoesNotOverwriteExplicitBrokerSocket()
    {
        var options = new DataverseOptions { TokenBrokerSocket = "/tmp/explicit.sock" };
        DataverseOptionsBinder.Bind(options, Config(("DataverseTokenBrokerSocket", "/tmp/configured.sock")));

        Assert.Equal("/tmp/explicit.sock", options.TokenBrokerSocket);
    }

    [Fact]
    public void Bind_LeavesDefaultCredentialType_WhenKeyAbsent()
    {
        var options = new DataverseOptions();

        DataverseOptionsBinder.Bind(options, Config());

        Assert.Equal(DataverseCredentialType.InteractiveBrowserCredential, options.CredentialType);
    }

    [Theory]
    [InlineData("DataverseCredentialType")]
    [InlineData("DATAVERSE_CREDENTIAL_TYPE")]
    public void Bind_ThrowsForUnknownCredentialType(string key)
    {
        var options = new DataverseOptions();

        Assert.Throws<ArgumentException>(
            () => DataverseOptionsBinder.Bind(options, Config((key, "bogus"))));
    }
}
