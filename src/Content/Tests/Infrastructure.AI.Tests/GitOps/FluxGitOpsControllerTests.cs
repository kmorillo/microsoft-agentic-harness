using Domain.AI.Changes;
using Domain.AI.GitOps;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Infrastructure.AI.Egress;
using Infrastructure.AI.GitOps.Flux;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Tests <see cref="FluxGitOpsController"/> driven through a real
/// <see cref="FluxApiClient"/> backed by a mocked egress <see cref="HttpClient"/>.
/// Exercises drift detection, cluster-health mapping, and remediation planning
/// against the documented Flux status JSON shape.
/// </summary>
public sealed class FluxGitOpsControllerTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-06-08T12:00:00Z"));

    private FluxGitOpsController CreateSut(HttpClient httpClient, string activeController = "flux")
    {
        _httpClientFactory
            .Setup(f => f.CreateClient(EgressPolicyDelegatingHandler.ClientName))
            .Returns(httpClient);

        var config = GitOpsTestConfig.Monitor(GitOpsTestConfig.ValidAppConfig(activeController));
        var apiClient = new FluxApiClient(_httpClientFactory.Object, config, NullLogger<FluxApiClient>.Instance);
        return new FluxGitOpsController(apiClient, config, NullLogger<FluxGitOpsController>.Instance, _time);
    }

    [Fact]
    public void Kind_IsFlux()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));

        sut.Kind.Should().Be(GitOpsControllerKind.Flux);
    }

    [Fact]
    public async Task DetectDriftAsync_AllReadyAndRevisionsMatch_ReportsNoDrift()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """
                {"items":[{"name":"apps","namespace":"flux-system","path":"./apps","ready":true,
                "lastAppliedRevision":"sha-1","lastAttemptedRevision":"sha-1"}]}
                """,
            ["helmreleases"] = """{"items":[{"name":"ingress","namespace":"infra","ready":true}]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasDrift.Should().BeFalse();
        result.Value.DriftedResources.Should().BeEmpty();
        result.Value.ControllerKind.Should().Be(GitOpsControllerKind.Flux);
        result.Value.EvidenceHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DetectDriftAsync_KustomizationNotReady_ReportsDrift()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """
                {"items":[{"name":"apps","namespace":"flux-system","path":"./apps","ready":false,
                "suspended":false,"message":"reconciliation failed"}]}
                """,
            ["helmreleases"] = """{"items":[]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasDrift.Should().BeTrue();
        result.Value.DriftedResources.Should().ContainSingle();
        var drifted = result.Value.DriftedResources[0];
        drifted.Kind.Should().Be("Kustomization");
        drifted.Name.Should().Be("apps");
        drifted.DesiredPath.Should().Be("./apps");
        drifted.Summary.Should().Be("reconciliation failed");
    }

    [Fact]
    public async Task DetectDriftAsync_RevisionMismatch_ReportsDrift()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """
                {"items":[{"name":"apps","namespace":"flux-system","path":"./apps","ready":true,
                "lastAppliedRevision":"sha-old","lastAttemptedRevision":"sha-new"}]}
                """,
            ["helmreleases"] = """{"items":[]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasDrift.Should().BeTrue();
        result.Value.DriftedResources.Should().ContainSingle(r => r.Kind == "Kustomization");
    }

    [Fact]
    public async Task DetectDriftAsync_SuspendedKustomization_NotTreatedAsDrift()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """
                {"items":[{"name":"apps","namespace":"flux-system","path":"./apps","ready":false,"suspended":true}]}
                """,
            ["helmreleases"] = """{"items":[]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasDrift.Should().BeFalse();
    }

    [Fact]
    public async Task DetectDriftAsync_HelmReleaseNotReady_ReportsHighSeverityDrift()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """{"items":[]}""",
            ["helmreleases"] = """{"items":[{"name":"ingress","namespace":"infra","ready":false,"message":"install failed"}]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var drifted = result.Value!.DriftedResources.Should().ContainSingle().Subject;
        drifted.Kind.Should().Be("HelmRelease");
        drifted.Severity.Should().Be(DriftSeverity.High);
    }

    [Fact]
    public async Task DetectDriftAsync_ApiUnreachable_ReturnsFailWithStableCode()
    {
        var sut = CreateSut(GitOpsHttpMock.UnreachableClient());

        var result = await sut.DetectDriftAsync(CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.flux.api_unreachable");
    }

    [Fact]
    public async Task GetClusterHealthAsync_AllReady_MapsToHealthy()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """{"items":[{"name":"apps","namespace":"flux-system","ready":true}]}""",
            ["helmreleases"] = """{"items":[{"name":"ingress","namespace":"infra","ready":true}]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.GetClusterHealthAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OverallStatus.Should().Be(ClusterHealthStatus.Healthy);
        result.Value.ResourceStates.Should().HaveCount(2);
        result.Value.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClusterHealthAsync_HelmReleaseNotReady_RollsUpToFailed()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """{"items":[{"name":"apps","namespace":"flux-system","ready":true}]}""",
            ["helmreleases"] = """{"items":[{"name":"ingress","namespace":"infra","ready":false}]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.GetClusterHealthAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OverallStatus.Should().Be(ClusterHealthStatus.Failed);
    }

    [Fact]
    public async Task GetClusterHealthAsync_SuspendedResource_SurfacesNote()
    {
        var client = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = """{"items":[{"name":"apps","namespace":"flux-system","ready":false,"suspended":true}]}""",
            ["helmreleases"] = """{"items":[]}"""
        });
        var sut = CreateSut(client);

        var result = await sut.GetClusterHealthAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Notes.Should().ContainSingle(n => n.Contains("suspended"));
        result.Value.OverallStatus.Should().Be(ClusterHealthStatus.Progressing);
    }

    [Fact]
    public async Task ProposeRemediationAsync_DriftWithDesiredPath_ProducesPlan()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));
        var drift = new DriftReport
        {
            ControllerKind = GitOpsControllerKind.Flux,
            CapturedAt = _time.GetUtcNow(),
            DriftedResources =
            [
                new DriftedResource
                {
                    ApiVersion = "kustomize.toolkit.fluxcd.io/v1",
                    Kind = "Kustomization",
                    Name = "apps",
                    Namespace = "flux-system",
                    DesiredPath = "./apps",
                    Severity = DriftSeverity.Medium
                }
            ]
        };

        var result = await sut.ProposeRemediationAsync(drift, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var plan = result.Value!;
        plan.ControllerKind.Should().Be(GitOpsControllerKind.Flux);
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Target.Should().Be("./apps");
        plan.Edits[0].Op.Should().Be(EditOp.Replace);
        plan.ProposedBlastRadius.Should().Be(BlastRadius.Medium);
        plan.Target.Should().BeOfType<GitRepoTarget>();
        ((GitRepoTarget)plan.Target).Branch.Should().Be("main");
    }

    [Fact]
    public async Task ProposeRemediationAsync_NoDrift_ReturnsFail()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));
        var drift = new DriftReport
        {
            ControllerKind = GitOpsControllerKind.Flux,
            CapturedAt = _time.GetUtcNow(),
            DriftedResources = []
        };

        var result = await sut.ProposeRemediationAsync(drift, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.remediation.no_drift");
    }

    [Fact]
    public async Task ProposeRemediationAsync_ControllerMismatch_ReturnsFail()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));
        var drift = new DriftReport
        {
            ControllerKind = GitOpsControllerKind.ArgoCd,
            CapturedAt = _time.GetUtcNow(),
            DriftedResources =
            [
                new DriftedResource { ApiVersion = "v1", Kind = "X", Name = "x", DesiredPath = "p" }
            ]
        };

        var result = await sut.ProposeRemediationAsync(drift, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.remediation.controller_mismatch");
    }

    [Fact]
    public async Task ProposeRemediationAsync_DriftWithoutDesiredPath_ReturnsNoRemediableDrift()
    {
        var sut = CreateSut(GitOpsHttpMock.JsonClient(System.Net.HttpStatusCode.OK, """{"items":[]}"""));
        var drift = new DriftReport
        {
            ControllerKind = GitOpsControllerKind.Flux,
            CapturedAt = _time.GetUtcNow(),
            DriftedResources =
            [
                new DriftedResource
                {
                    ApiVersion = "helm.toolkit.fluxcd.io/v2",
                    Kind = "HelmRelease",
                    Name = "ingress",
                    Namespace = "infra",
                    DesiredPath = null,
                    Severity = DriftSeverity.High
                }
            ]
        };

        var result = await sut.ProposeRemediationAsync(drift, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.remediation.no_remediable_drift");
    }
}
