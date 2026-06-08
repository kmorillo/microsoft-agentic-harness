using Application.AI.Common.Interfaces.GitOps;
using Domain.AI.Changes;
using Domain.AI.GitOps;
using Domain.AI.Identity;
using Domain.AI.SkillTraining;
using Domain.Common;
using FluentAssertions;
using Infrastructure.AI.Tools.GitOps;
using Moq;
using Xunit;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Tests the four agent-facing GitOps tools against mocked
/// <see cref="IGitOpsController"/>, <see cref="IGitOpsRemediationDispatcher"/>,
/// and <see cref="IK8sGptMcpClient"/> collaborators. Each tool is asserted on its
/// happy path (JSON returned), its failure pass-through, and its unknown-operation
/// rejection.
/// </summary>
public sealed class GitOpsToolsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-08T12:00:00Z");

    private static DriftReport DriftWith(params DriftedResource[] resources) => new()
    {
        ControllerKind = GitOpsControllerKind.Flux,
        CapturedAt = Now,
        DriftedResources = resources
    };

    private static DriftedResource SampleDrifted() => new()
    {
        ApiVersion = "kustomize.toolkit.fluxcd.io/v1",
        Kind = "Kustomization",
        Name = "apps",
        Namespace = "flux-system",
        DesiredPath = "./apps",
        Severity = DriftSeverity.Medium
    };

    private static RemediationPlan SamplePlan() => new()
    {
        ControllerKind = GitOpsControllerKind.Flux,
        SourceDrift = DriftWith(SampleDrifted()),
        Target = new GitRepoTarget("https://github.com/example/cluster-config.git", "main"),
        Edits = [new ChangeEdit { Op = EditOp.Replace, Target = "./apps", Content = "# fix" }],
        ProposedBlastRadius = BlastRadius.Medium
    };

    private static ChangeProposal SampleProposal() => new()
    {
        Id = "cp-1",
        Target = new GitRepoTarget("https://github.com/example/cluster-config.git", "main"),
        Diff = [new ChangeEdit { Op = EditOp.Replace, Target = "./apps", Content = "# fix" }],
        BlastRadius = BlastRadius.Medium,
        RequiredGates = [],
        Status = ChangeProposalStatus.Draft,
        SubmittedBy = new AgentIdentity { Id = "agent-001", Kind = AgentIdentityKind.Development },
        SubmittedAt = Now
    };

    private static IReadOnlyDictionary<string, object?> NoParams() => new Dictionary<string, object?>();

    // ---------- GitOpsDetectDriftTool ----------

    [Fact]
    public async Task DetectDriftTool_HappyPath_ReturnsOkWithJson()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.DetectDriftAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DriftReport>.Success(DriftWith(SampleDrifted())));
        var tool = new GitOpsDetectDriftTool(controller.Object);

        var result = await tool.ExecuteAsync("detect", NoParams());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Kustomization");
    }

    [Fact]
    public async Task DetectDriftTool_ControllerFailure_ReturnsFail()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.DetectDriftAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DriftReport>.Fail("gitops.flux.api_unreachable"));
        var tool = new GitOpsDetectDriftTool(controller.Object);

        var result = await tool.ExecuteAsync("detect", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("gitops.flux.api_unreachable");
    }

    [Fact]
    public async Task DetectDriftTool_UnknownOperation_ReturnsFail()
    {
        var tool = new GitOpsDetectDriftTool(Mock.Of<IGitOpsController>());

        var result = await tool.ExecuteAsync("frobnicate", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown operation");
    }

    [Fact]
    public void DetectDriftTool_IsReadOnlyAndConcurrencySafe()
    {
        var tool = new GitOpsDetectDriftTool(Mock.Of<IGitOpsController>());

        tool.Name.Should().Be("detect_drift");
        tool.IsReadOnly.Should().BeTrue();
        tool.IsConcurrencySafe.Should().BeTrue();
    }

    // ---------- GitOpsClusterHealthTool ----------

    [Fact]
    public async Task ClusterHealthTool_HappyPath_ReturnsOkWithJson()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.GetClusterHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClusterHealth>.Success(new ClusterHealth
            {
                ControllerKind = GitOpsControllerKind.Flux,
                CapturedAt = Now,
                OverallStatus = ClusterHealthStatus.Healthy
            }));
        var tool = new GitOpsClusterHealthTool(controller.Object);

        var result = await tool.ExecuteAsync("get", NoParams());

        result.Success.Should().BeTrue();
        // Enums serialize as their numeric value by default; OverallStatus=Healthy => 1.
        result.Output.Should().Contain("\"OverallStatus\":1");
    }

    [Fact]
    public async Task ClusterHealthTool_Failure_ReturnsFail()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.GetClusterHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClusterHealth>.Fail("gitops.argocd.api_unreachable"));
        var tool = new GitOpsClusterHealthTool(controller.Object);

        var result = await tool.ExecuteAsync("get", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("gitops.argocd.api_unreachable");
    }

    [Fact]
    public async Task ClusterHealthTool_UnknownOperation_ReturnsFail()
    {
        var tool = new GitOpsClusterHealthTool(Mock.Of<IGitOpsController>());

        var result = await tool.ExecuteAsync("bogus", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown operation");
    }

    // ---------- GitOpsProposeRemediationTool ----------

    [Fact]
    public async Task ProposeRemediationTool_NoDrift_ShortCircuitsWithOk()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.DetectDriftAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DriftReport>.Success(DriftWith()));
        var dispatcher = new Mock<IGitOpsRemediationDispatcher>();
        var tool = new GitOpsProposeRemediationTool(controller.Object, dispatcher.Object);

        var result = await tool.ExecuteAsync("submit", NoParams());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("No drift");
        controller.Verify(c => c.ProposeRemediationAsync(It.IsAny<DriftReport>(), It.IsAny<CancellationToken>()), Times.Never);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<RemediationPlan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProposeRemediationTool_DriftThenProposeThenDispatch_ReturnsOkWithProposalJson()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.DetectDriftAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DriftReport>.Success(DriftWith(SampleDrifted())));
        controller.Setup(c => c.ProposeRemediationAsync(It.IsAny<DriftReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RemediationPlan>.Success(SamplePlan()));
        var dispatcher = new Mock<IGitOpsRemediationDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<RemediationPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChangeProposal>.Success(SampleProposal()));
        var tool = new GitOpsProposeRemediationTool(controller.Object, dispatcher.Object);

        var result = await tool.ExecuteAsync("submit", NoParams());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("cp-1");
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<RemediationPlan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProposeRemediationTool_DetectFails_ReturnsFail()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.DetectDriftAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DriftReport>.Fail("gitops.flux.api_unreachable"));
        var tool = new GitOpsProposeRemediationTool(controller.Object, Mock.Of<IGitOpsRemediationDispatcher>());

        var result = await tool.ExecuteAsync("submit", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("gitops.flux.api_unreachable");
    }

    [Fact]
    public async Task ProposeRemediationTool_DispatchFails_ReturnsFail()
    {
        var controller = new Mock<IGitOpsController>();
        controller.Setup(c => c.DetectDriftAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DriftReport>.Success(DriftWith(SampleDrifted())));
        controller.Setup(c => c.ProposeRemediationAsync(It.IsAny<DriftReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RemediationPlan>.Success(SamplePlan()));
        var dispatcher = new Mock<IGitOpsRemediationDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<RemediationPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChangeProposal>.Fail("gitops.remediation.dispatch_failed"));
        var tool = new GitOpsProposeRemediationTool(controller.Object, dispatcher.Object);

        var result = await tool.ExecuteAsync("submit", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("gitops.remediation.dispatch_failed");
    }

    [Fact]
    public async Task ProposeRemediationTool_UnknownOperation_ReturnsFail()
    {
        var tool = new GitOpsProposeRemediationTool(Mock.Of<IGitOpsController>(), Mock.Of<IGitOpsRemediationDispatcher>());

        var result = await tool.ExecuteAsync("apply", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown operation");
    }

    [Fact]
    public void ProposeRemediationTool_IsNotReadOnly()
    {
        var tool = new GitOpsProposeRemediationTool(Mock.Of<IGitOpsController>(), Mock.Of<IGitOpsRemediationDispatcher>());

        tool.Name.Should().Be("propose_remediation");
        tool.IsReadOnly.Should().BeFalse();
        tool.IsConcurrencySafe.Should().BeFalse();
    }

    // ---------- K8sGptAnalyzeTool ----------

    [Fact]
    public async Task K8sGptAnalyzeTool_HappyPath_ReturnsOkWithJson()
    {
        var client = new Mock<IK8sGptMcpClient>();
        client.Setup(c => c.AnalyzeAsync(It.IsAny<K8sGptAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<K8sGptAnalysisResult>.Success(new K8sGptAnalysisResult
            {
                CapturedAt = Now,
                Findings = [new K8sGptFinding { Kind = "Pod", Name = "web", Summary = "ImagePullBackOff" }]
            }));
        var tool = new K8sGptAnalyzeTool(client.Object);

        var result = await tool.ExecuteAsync("analyze", NoParams());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("ImagePullBackOff");
    }

    [Fact]
    public async Task K8sGptAnalyzeTool_ClientFailure_ReturnsFail()
    {
        var client = new Mock<IK8sGptMcpClient>();
        client.Setup(c => c.AnalyzeAsync(It.IsAny<K8sGptAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<K8sGptAnalysisResult>.Fail("gitops.k8sgpt.unexpected_error"));
        var tool = new K8sGptAnalyzeTool(client.Object);

        var result = await tool.ExecuteAsync("analyze", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("gitops.k8sgpt.unexpected_error");
    }

    [Fact]
    public async Task K8sGptAnalyzeTool_UnknownOperation_ReturnsFail()
    {
        var tool = new K8sGptAnalyzeTool(Mock.Of<IK8sGptMcpClient>());

        var result = await tool.ExecuteAsync("mutate", NoParams());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown operation");
    }

    [Fact]
    public async Task K8sGptAnalyzeTool_ParsesNamespaceCsvFiltersAndExplain()
    {
        K8sGptAnalysisRequest? captured = null;
        var client = new Mock<IK8sGptMcpClient>();
        client.Setup(c => c.AnalyzeAsync(It.IsAny<K8sGptAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Callback<K8sGptAnalysisRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(Result<K8sGptAnalysisResult>.Success(new K8sGptAnalysisResult { CapturedAt = Now }));
        var tool = new K8sGptAnalyzeTool(client.Object);

        var parameters = new Dictionary<string, object?>
        {
            ["namespace"] = "prod",
            ["filters"] = "Deployment, Pod",
            ["explain"] = false
        };
        var result = await tool.ExecuteAsync("analyze", parameters);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Namespace.Should().Be("prod");
        captured.Filters.Should().BeEquivalentTo("Deployment", "Pod");
        captured.Explain.Should().BeFalse();
    }

    [Fact]
    public async Task K8sGptAnalyzeTool_ParsesFiltersAsArray()
    {
        K8sGptAnalysisRequest? captured = null;
        var client = new Mock<IK8sGptMcpClient>();
        client.Setup(c => c.AnalyzeAsync(It.IsAny<K8sGptAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Callback<K8sGptAnalysisRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(Result<K8sGptAnalysisResult>.Success(new K8sGptAnalysisResult { CapturedAt = Now }));
        var tool = new K8sGptAnalyzeTool(client.Object);

        var parameters = new Dictionary<string, object?>
        {
            ["filters"] = new[] { "Service", "Ingress" }
        };
        var result = await tool.ExecuteAsync("analyze", parameters);

        result.Success.Should().BeTrue();
        captured!.Filters.Should().BeEquivalentTo("Service", "Ingress");
    }

    [Fact]
    public async Task K8sGptAnalyzeTool_DefaultsExplainToTrueWhenOmitted()
    {
        K8sGptAnalysisRequest? captured = null;
        var client = new Mock<IK8sGptMcpClient>();
        client.Setup(c => c.AnalyzeAsync(It.IsAny<K8sGptAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Callback<K8sGptAnalysisRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(Result<K8sGptAnalysisResult>.Success(new K8sGptAnalysisResult { CapturedAt = Now }));
        var tool = new K8sGptAnalyzeTool(client.Object);

        await tool.ExecuteAsync("analyze", NoParams());

        captured!.Explain.Should().BeTrue();
        captured.Namespace.Should().BeNull();
        captured.Filters.Should().BeEmpty();
    }
}
