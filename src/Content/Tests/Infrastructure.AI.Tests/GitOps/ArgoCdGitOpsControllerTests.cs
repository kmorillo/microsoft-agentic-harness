using Domain.AI.Changes;
using Domain.AI.GitOps;
using FluentAssertions;
using Infrastructure.AI.Egress;
using Infrastructure.AI.GitOps.ArgoCd;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Tests <see cref="ArgoCdGitOpsController"/> driven through a real
/// <see cref="ArgoCdApiClient"/> backed by a mocked egress <see cref="HttpClient"/>.
/// Exercises drift detection (OutOfSync / Degraded / Missing), cluster-health
/// mapping, and remediation planning against the Argo CD Application JSON shape.
/// </summary>
public sealed class ArgoCdGitOpsControllerTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-06-08T12:00:00Z"));

    private ArgoCdGitOpsController CreateSut(HttpClient httpClient)
    {
        _httpClientFactory
            .Setup(f => f.CreateClient(EgressPolicyDelegatingHandler.ClientName))
            .Returns(httpClient);

        var config = GitOpsTestConfig.Monitor(GitOpsTestConfig.ValidAppConfig("argocd"));
        var apiClient = new ArgoCdApiClient(_httpClientFactory.Object, config, NullLogger<ArgoCdApiClient>.Instance);
        return new ArgoCdGitOpsController(apiClient, config, NullLogger<ArgoCdGitOpsController>.Instance, _time);
    }

    private static string AppsJson(params string[] appItems)
        => "{\"items\":[" + string.Join(",", appItems) + "]}";

    private static string App(string name, string sync, string health, string path = "apps", string message = "")
        => "{" +
           "\"metadata\":{\"name\":\"" + name + "\",\"namespace\":\"argocd\"}," +
           "\"spec\":{\"source\":{\"repoURL\":\"https://github.com/example/cluster-config.git\",\"path\":\"" + path + "\"}}," +
           "\"status\":{\"sync\":{\"status\":\"" + sync + "\",\"revision\":\"r1\"}," +
           "\"health\":{\"status\":\"" + health + "\",\"message\":\"" + message + "\"}}}";

    [Fact]
    public void Kind_IsArgoCd()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));

        sut.Kind.Should().Be(GitOpsControllerKind.ArgoCd);
    }

    [Fact]
    public async Task DetectDriftAsync_SyncedAndHealthy_ReportsNoDrift()
    {
        var client = GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, AppsJson(App("web", "Synced", "Healthy")));
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasDrift.Should().BeFalse();
        result.Value.ControllerKind.Should().Be(GitOpsControllerKind.ArgoCd);
    }

    [Fact]
    public async Task DetectDriftAsync_OutOfSync_ReportsDrift()
    {
        var client = GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, AppsJson(App("web", "OutOfSync", "Healthy")));
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var drifted = result.Value!.DriftedResources.Should().ContainSingle().Subject;
        drifted.Kind.Should().Be("Application");
        drifted.Name.Should().Be("web");
        drifted.DesiredPath.Should().Be("apps");
        drifted.Severity.Should().Be(DriftSeverity.Medium);
    }

    [Fact]
    public async Task DetectDriftAsync_DegradedHealth_ReportsHighSeverityDrift()
    {
        var client = GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, AppsJson(App("api", "Synced", "Degraded", message: "crashloop")));
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var drifted = result.Value!.DriftedResources.Should().ContainSingle().Subject;
        drifted.Severity.Should().Be(DriftSeverity.High);
        drifted.Summary.Should().Be("crashloop");
    }

    [Fact]
    public async Task DetectDriftAsync_MissingHealth_ReportsHighSeverityDrift()
    {
        var client = GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, AppsJson(App("api", "Synced", "Missing")));
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DriftedResources.Should().ContainSingle(r => r.Severity == DriftSeverity.High);
    }

    [Fact]
    public async Task DetectDriftAsync_ApiUnreachable_ReturnsFailWithStableCode()
    {
        var sut = CreateSut(GitOpsHttpMock.UnreachableClient());

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.argocd.api_unreachable");
    }

    [Fact]
    public async Task GetClusterHealthAsync_SyncedHealthy_MapsToHealthy()
    {
        var client = GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, AppsJson(App("web", "Synced", "Healthy")));
        var sut = CreateSut(client);

        var result = await sut.GetClusterHealthAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OverallStatus.Should().Be(ClusterHealthStatus.Healthy);
        result.Value.ResourceStates.Should().ContainSingle(r => r.Kind == "Application");
    }

    [Fact]
    public async Task GetClusterHealthAsync_Degraded_RollsUpToDegraded()
    {
        var client = GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK,
            AppsJson(App("web", "Synced", "Healthy"), App("api", "OutOfSync", "Degraded")));
        var sut = CreateSut(client);

        var result = await sut.GetClusterHealthAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OverallStatus.Should().Be(ClusterHealthStatus.Degraded);
    }

    [Fact]
    public async Task GetClusterHealthAsync_Suspended_SurfacesNote()
    {
        var client = GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, AppsJson(App("web", "Synced", "Suspended")));
        var sut = CreateSut(client);

        var result = await sut.GetClusterHealthAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Notes.Should().ContainSingle(n => n.Contains("suspended"));
        result.Value.OverallStatus.Should().Be(ClusterHealthStatus.Progressing);
    }

    [Fact]
    public async Task ProposeRemediationAsync_Drift_ProducesPlanWithArgoCdKind()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));
        var drift = new DriftReport
        {
            ControllerKind = GitOpsControllerKind.ArgoCd,
            CapturedAt = _time.GetUtcNow(),
            DriftedResources =
            [
                new DriftedResource
                {
                    ApiVersion = "argoproj.io/v1alpha1",
                    Kind = "Application",
                    Name = "web",
                    Namespace = "argocd",
                    DesiredPath = "apps",
                    Severity = DriftSeverity.High
                }
            ]
        };

        var result = await sut.ProposeRemediationAsync(drift, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ControllerKind.Should().Be(GitOpsControllerKind.ArgoCd);
        result.Value.ProposedBlastRadius.Should().Be(BlastRadius.High);
        result.Value.Edits.Should().ContainSingle(e => e.Target == "apps");
    }

    [Fact]
    public async Task ProposeRemediationAsync_NoDrift_ReturnsFail()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));
        var drift = new DriftReport
        {
            ControllerKind = GitOpsControllerKind.ArgoCd,
            CapturedAt = _time.GetUtcNow(),
            DriftedResources = []
        };

        var result = await sut.ProposeRemediationAsync(drift, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.remediation.no_drift");
    }
}
