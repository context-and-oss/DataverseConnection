using Azure.Core;
using Azure.Identity;
using DataverseConnection.Internal;
using Xunit;

namespace DataverseConnection.Tests;

public class DataverseCredentialFactoryTests
{
    [Fact]
    public void DefaultCredentialType_IsInteractiveBrowser()
    {
        Assert.Equal(
            DataverseCredentialType.InteractiveBrowserCredential,
            new DataverseOptions().CredentialType);
    }

    [Fact]
    public void Create_ReturnsAzureCliCredential()
    {
        var options = new DataverseOptions
        {
            CredentialType = DataverseCredentialType.AzureCliCredential
        };

        var credential = DataverseCredentialFactory.Create(options);

        Assert.IsType<AzureCliCredential>(credential);
    }

    [Fact]
    public void Create_ReturnsDeviceCodeCredential_WhenCallerSuppliesOptions()
    {
        var options = new DataverseOptions
        {
            CredentialType = DataverseCredentialType.DeviceCodeCredential,
            DeviceCodeCredentialOptions = new DeviceCodeCredentialOptions()
        };

        var credential = DataverseCredentialFactory.Create(options);

        Assert.IsType<DeviceCodeCredential>(credential);
    }

    [Fact]
    public void Create_ReturnsInteractiveBrowserCredential_WhenCallerSuppliesOptions()
    {
        var options = new DataverseOptions
        {
            CredentialType = DataverseCredentialType.InteractiveBrowserCredential,
            InteractiveBrowserCredentialOptions = new InteractiveBrowserCredentialOptions()
        };

        var credential = DataverseCredentialFactory.Create(options);

        Assert.IsType<InteractiveBrowserCredential>(credential);
    }

    [Theory]
    [InlineData(DataverseCredentialType.DeviceCodeCredential)]
    [InlineData(DataverseCredentialType.InteractiveBrowserCredential)]
    public void Create_WrapsInteractiveCredentials_WithPersistentCache_ByDefault(
        DataverseCredentialType credentialType)
    {
        var options = new DataverseOptions
        {
            DataverseUrl = "https://org.crm4.dynamics.com",
            CredentialType = credentialType
        };

        var credential = DataverseCredentialFactory.Create(options, options.DataverseUrl);

        // The interactive credentials are wrapped so tokens persist across runs by default.
        Assert.IsType<PersistentAuthCredential>(credential);
    }

    [Fact]
    public void PersistentCache_AllowsFileFallback_OnLinux()
    {
        var options = PersistentCredentialCache.CreateTokenCachePersistenceOptions("test", isLinux: true);

        Assert.True(options.UnsafeAllowUnencryptedStorage);
    }

    [Fact]
    public void PersistentCache_RequiresEncryptedStorage_OnOtherOperatingSystems()
    {
        var options = PersistentCredentialCache.CreateTokenCachePersistenceOptions("test", isLinux: false);

        Assert.False(options.UnsafeAllowUnencryptedStorage);
    }

    [Fact]
    public void Create_UsesSamePersistentCredentialKey_ForSameEnvironmentUrl()
    {
        var first = Assert.IsType<PersistentAuthCredential>(
            DataverseCredentialFactory.Create(
                new DataverseOptions(),
                "https://ORG.crm4.dynamics.com/"));
        var second = Assert.IsType<PersistentAuthCredential>(
            DataverseCredentialFactory.Create(
                new DataverseOptions(),
                "https://org.crm4.dynamics.com/some/path"));

        Assert.Equal(first.PersistenceKey, second.PersistenceKey);
    }

    [Fact]
    public void Create_UsesDifferentPersistentCredentialKeys_ForDifferentEnvironmentUrls()
    {
        var first = Assert.IsType<PersistentAuthCredential>(
            DataverseCredentialFactory.Create(
                new DataverseOptions(),
                "https://first.crm4.dynamics.com"));
        var second = Assert.IsType<PersistentAuthCredential>(
            DataverseCredentialFactory.Create(
                new DataverseOptions(),
                "https://second.crm4.dynamics.com"));

        Assert.NotEqual(first.PersistenceKey, second.PersistenceKey);
    }

    [Fact]
    public void Create_UsesDifferentPersistentCredentialKeys_ForDifferentCredentialTypes()
    {
        const string url = "https://org.crm4.dynamics.com";
        var browser = Assert.IsType<PersistentAuthCredential>(
            DataverseCredentialFactory.Create(new DataverseOptions(), url));
        var deviceCode = Assert.IsType<PersistentAuthCredential>(
            DataverseCredentialFactory.Create(
                new DataverseOptions { CredentialType = DataverseCredentialType.DeviceCodeCredential },
                url));

        Assert.NotEqual(browser.PersistenceKey, deviceCode.PersistenceKey);
    }

    [Theory]
    [InlineData(DataverseCredentialType.DeviceCodeCredential)]
    [InlineData(DataverseCredentialType.InteractiveBrowserCredential)]
    public void Create_RequiresUrl_ForDefaultPersistentCredentials(DataverseCredentialType credentialType)
    {
        var options = new DataverseOptions { CredentialType = credentialType };

        var exception = Assert.Throws<InvalidOperationException>(
            () => DataverseCredentialFactory.Create(options));

        Assert.Contains("DataverseUrl", exception.Message);
    }

    [Fact]
    public void Create_ReturnsBrokerCredential()
    {
        var options = new DataverseOptions
        {
            DataverseUrl = "https://org.crm4.dynamics.com",
            CredentialType = DataverseCredentialType.Broker,
            TokenBrokerSocket = "/tmp/dataverse-broker.sock"
        };

        Assert.IsType<BrokerTokenCredential>(DataverseCredentialFactory.Create(options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_BrokerWithoutSocket_FailsClosed(string? socket)
    {
        var options = new DataverseOptions
        {
            CredentialType = DataverseCredentialType.Broker,
            TokenBrokerSocket = socket
        };

        if (socket is null)
            Assert.Throws<InvalidOperationException>(() =>
                DataverseCredentialFactory.Create(options, "https://org.crm4.dynamics.com"));
        else
            Assert.Throws<ArgumentException>(() =>
                DataverseCredentialFactory.Create(options, "https://org.crm4.dynamics.com"));
    }

    [Theory]
    [InlineData(DataverseCredentialType.AzureCliCredential)]
    [InlineData(DataverseCredentialType.Broker)]
    public void Create_UsesExplicitTokenCredentialBeforeSelectedType(DataverseCredentialType credentialType)
    {
        var explicitCredential = new TestTokenCredential();
        var options = new DataverseOptions
        {
            CredentialType = credentialType,
            TokenCredential = explicitCredential,
            // These broker settings must not be validated when an explicit credential is supplied.
            BrokerTokenCredentialOptions = new BrokerTokenCredentialOptions { Timeout = TimeSpan.Zero }
        };

        var credential = DataverseCredentialFactory.Create(options);

        Assert.Same(explicitCredential, credential);
    }

    [Fact]
    public void Create_ThrowsForUnsupportedCredentialType()
    {
        var options = new DataverseOptions
        {
            CredentialType = (DataverseCredentialType)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => DataverseCredentialFactory.Create(options));
    }

    private sealed class TestTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
