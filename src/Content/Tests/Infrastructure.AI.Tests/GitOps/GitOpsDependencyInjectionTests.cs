using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.GitOps;
using Application.AI.Common.Interfaces.Tools;
using Domain.AI.GitOps;
using Domain.Common.Config;
using FluentAssertions;
using Infrastructure.AI.GitOps.ArgoCd;
using Infrastructure.AI.GitOps.Flux;
using Infrastructure.AI.Tools.GitOps;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Verifies <see cref="GitOpsDependencyInjection.AddGitOpsSkillTools"/> registers
/// the four tools by keyed name, both controllers by controller key, and a default
/// <see cref="IGitOpsController"/> that resolves the configured active controller.
/// Mirrors <c>WorkspaceDependencyInjectionTests</c>.
/// </summary>
public sealed class GitOpsDependencyInjectionTests
{
    private static ServiceCollection BaseServices(string activeController)
    {
        var services = new ServiceCollection();

        // External dependencies the GitOps services consume but this DI module does not own.
        services.AddSingleton(GitOpsTestConfig.Monitor(GitOpsTestConfig.ValidAppConfig(activeController)));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<IMediator>());
        services.AddSingleton(Mock.Of<IMcpToolProvider>());
        services.AddSingleton(Mock.Of<IHttpClientFactory>());
        services.AddLogging();
        return services;
    }

    [Fact]
    public void AddGitOpsSkillTools_RegistersAllFourToolsByKeyedName()
    {
        var services = BaseServices("flux");

        services.AddGitOpsSkillTools();
        var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<ITool>(GitOpsDetectDriftTool.ToolName).Should().BeOfType<GitOpsDetectDriftTool>();
        sp.GetRequiredKeyedService<ITool>(GitOpsClusterHealthTool.ToolName).Should().BeOfType<GitOpsClusterHealthTool>();
        sp.GetRequiredKeyedService<ITool>(GitOpsProposeRemediationTool.ToolName).Should().BeOfType<GitOpsProposeRemediationTool>();
        sp.GetRequiredKeyedService<ITool>(K8sGptAnalyzeTool.ToolName).Should().BeOfType<K8sGptAnalyzeTool>();
    }

    [Fact]
    public void AddGitOpsSkillTools_RegistersBothControllersByKey()
    {
        var services = BaseServices("flux");

        services.AddGitOpsSkillTools();
        var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<IGitOpsController>("flux").Should().BeOfType<FluxGitOpsController>();
        sp.GetRequiredKeyedService<IGitOpsController>("argocd").Should().BeOfType<ArgoCdGitOpsController>();
    }

    [Fact]
    public void AddGitOpsSkillTools_DefaultController_ResolvesActiveFlux()
    {
        var services = BaseServices("flux");

        services.AddGitOpsSkillTools();
        var sp = services.BuildServiceProvider();

        var controller = sp.GetRequiredService<IGitOpsController>();
        controller.Kind.Should().Be(GitOpsControllerKind.Flux);
    }

    [Fact]
    public void AddGitOpsSkillTools_DefaultController_ResolvesActiveArgoCd()
    {
        var services = BaseServices("argocd");

        services.AddGitOpsSkillTools();
        var sp = services.BuildServiceProvider();

        var controller = sp.GetRequiredService<IGitOpsController>();
        controller.Kind.Should().Be(GitOpsControllerKind.ArgoCd);
    }

    [Fact]
    public void AddGitOpsSkillTools_DefaultController_ThrowsWhenActiveControllerBlank()
    {
        var services = new ServiceCollection();
        services.AddSingleton(GitOpsTestConfig.Monitor(GitOpsTestConfig.ValidAppConfig(activeController: "")));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<IMediator>());
        services.AddSingleton(Mock.Of<IMcpToolProvider>());
        services.AddSingleton(Mock.Of<IHttpClientFactory>());
        services.AddLogging();

        services.AddGitOpsSkillTools();
        var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IGitOpsController>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ActiveController*");
    }

    [Fact]
    public void AddGitOpsSkillTools_RegistersK8sGptClientAndDispatcher()
    {
        var services = BaseServices("flux");

        services.AddGitOpsSkillTools();
        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IK8sGptMcpClient>().Should().NotBeNull();
        sp.GetRequiredService<IGitOpsRemediationDispatcher>().Should().NotBeNull();
    }

    [Fact]
    public void AddGitOpsSkillTools_RegistersStartupValidatorAsHostedService()
    {
        var services = BaseServices("flux");

        services.AddGitOpsSkillTools();
        var sp = services.BuildServiceProvider();

        sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should().ContainSingle(h => h is Infrastructure.AI.GitOps.GitOpsStartupValidator);
    }
}
