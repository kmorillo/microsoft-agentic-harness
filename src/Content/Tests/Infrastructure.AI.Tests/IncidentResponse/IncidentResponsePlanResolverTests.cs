using Domain.Common.Config;
using Domain.Common.Config.AI.IncidentResponse;
using FluentAssertions;
using Infrastructure.AI.IncidentResponse;
using Infrastructure.AI.Tests.Changes.Support;
using Xunit;

namespace Infrastructure.AI.Tests.IncidentResponse;

/// <summary>
/// Unit tests for <see cref="IncidentResponsePlanResolver"/>. Covers match,
/// case-insensitivity, default-fallback, miss, and hot-reload behaviour.
/// </summary>
public sealed class IncidentResponsePlanResolverTests
{
    private static AppConfig MakeConfig(
        IReadOnlyList<IncidentResponsePlan> plans,
        string? defaultPlanName = null)
    {
        var cfg = new AppConfig();
        cfg.AI.IncidentResponse.Plans = [.. plans];
        cfg.AI.IncidentResponse.DefaultPlanName = defaultPlanName;
        return cfg;
    }

    private static IncidentResponsePlan Plan(
        string name,
        string incidentType,
        string? autonomyTier = null,
        params string[] additionalGates) =>
        new()
        {
            Name = name,
            IncidentType = incidentType,
            AutonomyTierOverride = autonomyTier,
            AdditionalRequiredGates = additionalGates
        };

    [Fact]
    public void ResolveFor_MatchingIncidentType_ReturnsPlan()
    {
        var data = Plan("DataExfil", "DataExfiltrationSuspected", "Restricted", "compliance");
        var cfg = MakeConfig([data, Plan("Rollback", "ProductionRollback")]);
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor("DataExfiltrationSuspected").Should().BeSameAs(data);
    }

    [Fact]
    public void ResolveFor_MatchingIncidentType_IsCaseInsensitive()
    {
        var data = Plan("DataExfil", "DataExfiltrationSuspected");
        var cfg = MakeConfig([data]);
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor("dataexfiltrationsuspected").Should().BeSameAs(data);
    }

    [Fact]
    public void ResolveFor_NoMatchAndDefaultConfigured_ReturnsDefaultPlan()
    {
        var standardOps = Plan("StandardOps", "StandardOps");
        var cfg = MakeConfig([standardOps], defaultPlanName: "StandardOps");
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor("UnknownIncident").Should().BeSameAs(standardOps);
    }

    [Fact]
    public void ResolveFor_NoMatchAndNoDefault_ReturnsNull()
    {
        var cfg = MakeConfig([Plan("DataExfil", "DataExfiltrationSuspected")]);
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor("UnknownIncident").Should().BeNull();
    }

    [Fact]
    public void ResolveFor_NullIncidentTypeAndNoDefault_ReturnsNull()
    {
        var cfg = MakeConfig([Plan("DataExfil", "DataExfiltrationSuspected")]);
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor(null).Should().BeNull();
        sut.ResolveFor("   ").Should().BeNull();
    }

    [Fact]
    public void ResolveFor_NullIncidentTypeAndDefaultConfigured_ReturnsDefaultPlan()
    {
        var standardOps = Plan("StandardOps", "StandardOps");
        var cfg = MakeConfig([standardOps], defaultPlanName: "StandardOps");
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor(null).Should().BeSameAs(standardOps);
    }

    [Fact]
    public void ResolveFor_EmptyRegistry_ReturnsNull()
    {
        var cfg = MakeConfig(plans: []);
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor("DataExfiltrationSuspected").Should().BeNull();
    }

    [Fact]
    public void ResolveFor_FirstMatchWins()
    {
        // Boot validator rejects duplicate types, but if a host runs without
        // the validator (or hot-loads duplicates after start), the resolver
        // returns the first declared. This documents that deterministic order.
        var first = Plan("First", "Same");
        var second = Plan("Second", "Same");
        var cfg = MakeConfig([first, second]);
        var sut = new IncidentResponsePlanResolver(new TestConfig.StaticOptionsMonitor<AppConfig>(cfg));

        sut.ResolveFor("Same").Should().BeSameAs(first);
    }

    [Fact]
    public void ResolveFor_HotReload_PicksUpNewPlanOnNextResolve()
    {
        var initial = MakeConfig([Plan("Old", "OldIncident")]);
        var monitor = new MutableOptionsMonitor<AppConfig>(initial);
        var sut = new IncidentResponsePlanResolver(monitor);

        sut.ResolveFor("OldIncident").Should().NotBeNull();
        sut.ResolveFor("NewIncident").Should().BeNull();

        var swapped = MakeConfig([Plan("New", "NewIncident")]);
        monitor.Set(swapped);

        sut.ResolveFor("NewIncident").Should().NotBeNull();
        sut.ResolveFor("OldIncident").Should().BeNull();
    }

    /// <summary>
    /// Test double for hot-reload that lets a test push a new
    /// <see cref="AppConfig"/> after the resolver has been constructed.
    /// </summary>
    private sealed class MutableOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private T _current;
        public MutableOptionsMonitor(T initial) { _current = initial; }
        public T CurrentValue => _current;
        public T Get(string? name) => _current;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
        public void Set(T next) => _current = next;
    }
}
