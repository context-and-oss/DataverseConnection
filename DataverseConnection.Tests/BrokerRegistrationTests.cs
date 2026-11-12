using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Xunit;

namespace DataverseConnection.Tests;

public class BrokerRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationOnlyBroker_MissingSocketFailsClosed(bool useFactory)
    {
        using var provider = Register(useFactory, socketPath: null);

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(provider, useFactory));
        Assert.Contains("DataverseTokenBrokerSocket", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationOnlyBroker_InvalidSocketFailsClosed(bool useFactory)
    {
        using var provider = Register(useFactory, socketPath: "relative.sock");

        var exception = Assert.Throws<ArgumentException>(() => Resolve(provider, useFactory));
        Assert.Equal("socketPath", exception.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgrammaticCallback_OverridesConfiguredBrokerSocket(bool useFactory)
    {
        using var provider = Register(useFactory, socketPath: "/tmp/configured.sock", options =>
        {
            Assert.Equal(DataverseCredentialType.Broker, options.CredentialType);
            Assert.Equal("/tmp/configured.sock", options.TokenBrokerSocket);
            options.TokenBrokerSocket = "invalid-relative-override.sock";
        });

        var exception = Assert.Throws<ArgumentException>(() => Resolve(provider, useFactory));
        Assert.Equal("socketPath", exception.ParamName);
    }

    private static ServiceProvider Register(
        bool useFactory, string? socketPath, Action<DataverseOptions>? configureOptions = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataverseUrl"] = "https://org.crm4.dynamics.com",
            ["DataverseCredentialType"] = "broker",
            ["DataverseTokenBrokerSocket"] = socketPath
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        if (useFactory)
            services.AddDataverseFactory(configureOptions);
        else
            services.AddDataverseWithOrganizationServices(configureOptions);
        return services.BuildServiceProvider();
    }

    private static void Resolve(ServiceProvider provider, bool useFactory)
    {
        if (useFactory)
            provider.GetRequiredService<IServiceClientFactory>().CreateClient();
        else
            provider.GetRequiredService<ServiceClient>();
    }
}
