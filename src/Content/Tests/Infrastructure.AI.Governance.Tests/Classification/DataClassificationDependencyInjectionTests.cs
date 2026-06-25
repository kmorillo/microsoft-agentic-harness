using Application.AI.Common.Interfaces.Governance;
using Application.AI.Common.Services.Governance;
using Domain.Common.Config.AI.Governance;
using FluentAssertions;
using Infrastructure.AI.Governance.Classification;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Infrastructure.AI.Governance.Tests.Classification;

/// <summary>
/// DI composition tests for the data-classification provider wiring in
/// <see cref="DependencyInjection.AddDataClassificationProvider"/>. They prove the three resolution
/// branches actually build: the enabled Information Protection path yields the cached Graph provider, and
/// both not-wired paths keep the fail-fast default. Resolving the service exercises the factory lambda,
/// which a pure registration test would not. The internal registration method is tested directly rather
/// than through <see cref="DependencyInjection.AddGovernanceDependencies"/>, which additionally stands up
/// a live Agent Governance Toolkit kernel that requires policy configuration unavailable to a unit test.
/// </summary>
public sealed class DataClassificationDependencyInjectionTests
{
    [Fact]
    public void AddDataClassificationProvider_IpProviderEnabled_ResolvesCachedGraphProvider()
    {
        var config = new DataClassificationConfig
        {
            Mode = ClassificationEnforcementMode.Enforce,
            InformationProtection = new InformationProtectionProviderConfig { Enabled = true },
        };
        using var sp = BuildProvider(config);

        sp.GetRequiredService<IDataClassificationProvider>()
            .Should().BeOfType<CachedDataClassificationProvider>();
    }

    [Fact]
    public void AddDataClassificationProvider_GateOnButNoProviderEnabled_ResolvesNotConfiguredDefault()
    {
        var config = new DataClassificationConfig { Mode = ClassificationEnforcementMode.Enforce };
        using var sp = BuildProvider(config);

        sp.GetRequiredService<IDataClassificationProvider>()
            .Should().BeOfType<NotConfiguredDataClassificationProvider>();
    }

    [Fact]
    public void AddDataClassificationProvider_DataMapProviderEnabled_ResolvesCachedRoutingProvider()
    {
        var config = new DataClassificationConfig
        {
            Mode = ClassificationEnforcementMode.Enforce,
            DataMap = new DataMapProviderConfig { Enabled = true, AccountEndpoint = "https://acct.purview.azure.com" },
        };
        using var sp = BuildProvider(config);

        sp.GetRequiredService<IDataClassificationProvider>()
            .Should().BeOfType<CachedDataClassificationProvider>();
    }

    [Fact]
    public void AddDataClassificationProvider_BothWorldsEnabled_ResolvesCachedRoutingProvider()
    {
        var config = new DataClassificationConfig
        {
            Mode = ClassificationEnforcementMode.Enforce,
            InformationProtection = new InformationProtectionProviderConfig { Enabled = true },
            DataMap = new DataMapProviderConfig { Enabled = true, AccountEndpoint = "https://acct.purview.azure.com" },
        };
        using var sp = BuildProvider(config);

        sp.GetRequiredService<IDataClassificationProvider>()
            .Should().BeOfType<CachedDataClassificationProvider>();
    }

    [Fact]
    public void AddDataClassificationProvider_GateOff_ResolvesNotConfiguredDefault()
    {
        var config = new DataClassificationConfig
        {
            Mode = ClassificationEnforcementMode.Off,
            InformationProtection = new InformationProtectionProviderConfig { Enabled = true },
        };
        using var sp = BuildProvider(config);

        sp.GetRequiredService<IDataClassificationProvider>()
            .Should().BeOfType<NotConfiguredDataClassificationProvider>();
    }

    private static ServiceProvider BuildProvider(DataClassificationConfig config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        DependencyInjection.AddDataClassificationProvider(services, config);
        return services.BuildServiceProvider();
    }
}
