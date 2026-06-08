using Application.AI.Common.CQRS.Changes.SubmitChangeProposal;
using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.Tools;
using Domain.AI.Changes;
using Domain.AI.Identity;
using Domain.Common;
using FluentAssertions;
using Infrastructure.AI.Egress;
using Infrastructure.AI.Tools.GitOps;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// End-to-end acceptance for the GitOps skill pack: wires
/// <see cref="GitOpsDependencyInjection.AddGitOpsSkillTools"/> on a real
/// <see cref="ServiceCollection"/>, resolves the agent-facing tools by keyed name,
/// and drives the detect→propose→dispatch flow through a mocked egress HttpClient
/// (drift source) and a captured <see cref="IMediator"/> (change-proposal sink).
/// Mirrors the structure of <c>WorkspaceSkillEndToEndAcceptanceTests</c>.
/// </summary>
public sealed class GitOpsSkillEndToEndAcceptanceTests
{
    private const string DriftedClusterJson = """
        {"items":[{"name":"apps","namespace":"flux-system","path":"./apps","ready":false,
        "suspended":false,"message":"reconciliation failed"}]}
        """;
    private const string HealthyClusterJson = """{"items":[]}""";

    private static ChangeProposal SampleProposal(SubmitChangeProposalCommand cmd) => new()
    {
        Id = "cp-e2e-1",
        Target = cmd.Target,
        Diff = cmd.Diff,
        BlastRadius = cmd.BlastRadius,
        RequiredGates = [],
        Status = ChangeProposalStatus.Draft,
        SubmittedBy = new AgentIdentity { Id = "agent-001", Kind = AgentIdentityKind.Development },
        SubmittedAt = DateTimeOffset.Parse("2026-06-08T12:00:00Z")
    };

    private static ServiceProvider BuildProvider(
        string kustomizationsJson,
        out Mock<IMediator> mediator)
    {
        var httpClient = GitOpsHttpMock.RoutedJsonClient(new Dictionary<string, string>
        {
            ["kustomizations"] = kustomizationsJson,
            ["helmreleases"] = """{"items":[]}"""
        });
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(EgressPolicyDelegatingHandler.ClientName)).Returns(httpClient);

        mediator = new Mock<IMediator>();

        var services = new ServiceCollection();
        services.AddSingleton(GitOpsTestConfig.Monitor(GitOpsTestConfig.ValidAppConfig("flux")));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(httpFactory.Object);
        services.AddSingleton(mediator.Object);
        services.AddSingleton(Mock.Of<IMcpToolProvider>());
        services.AddLogging();
        services.AddGitOpsSkillTools();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DetectThenProposeRemediation_DriftedCluster_SubmitsChangeProposalWithDriftedResource()
    {
        var sp = BuildProvider(DriftedClusterJson, out var mediator);

        SubmitChangeProposalCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<SubmitChangeProposalCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ChangeProposal>>, CancellationToken>((cmd, _) => captured = (SubmitChangeProposalCommand)cmd)
            .ReturnsAsync((IRequest<Result<ChangeProposal>> cmd, CancellationToken _) =>
                Result<ChangeProposal>.Success(SampleProposal((SubmitChangeProposalCommand)cmd)));

        // 1. detect_drift sees the drifted cluster.
        var detectTool = sp.GetRequiredKeyedService<ITool>(GitOpsDetectDriftTool.ToolName);
        var detect = await detectTool.ExecuteAsync("detect", new Dictionary<string, object?>());
        detect.Success.Should().BeTrue();
        detect.Output.Should().Contain("apps");

        // 2. propose_remediation runs detect→propose→dispatch through the pipeline.
        var proposeTool = sp.GetRequiredKeyedService<ITool>(GitOpsProposeRemediationTool.ToolName);
        var propose = await proposeTool.ExecuteAsync("submit", new Dictionary<string, object?>());

        propose.Success.Should().BeTrue();
        propose.Output.Should().Contain("cp-e2e-1");

        captured.Should().NotBeNull();
        captured!.IsStateChange.Should().BeTrue();
        captured.SkillKey.Should().Be("gitops:flux");
        captured.Target.Should().BeOfType<GitRepoTarget>();
        ((GitRepoTarget)captured.Target).RepoUrl.Should().Be("https://github.com/example/cluster-config.git");
        captured.Diff.Should().ContainSingle(e => e.Target == "./apps");
    }

    [Fact]
    public async Task ProposeRemediation_HealthyCluster_ShortCircuitsWithoutSubmitting()
    {
        var sp = BuildProvider(HealthyClusterJson, out var mediator);

        var proposeTool = sp.GetRequiredKeyedService<ITool>(GitOpsProposeRemediationTool.ToolName);
        var propose = await proposeTool.ExecuteAsync("submit", new Dictionary<string, object?>());

        propose.Success.Should().BeTrue();
        propose.Output.Should().Contain("No drift");
        mediator.Verify(
            m => m.Send(It.IsAny<SubmitChangeProposalCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
