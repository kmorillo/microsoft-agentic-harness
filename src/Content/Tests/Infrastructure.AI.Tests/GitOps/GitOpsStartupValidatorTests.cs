using Domain.Common.Config;
using FluentAssertions;
using Infrastructure.AI.GitOps;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Tests <see cref="GitOpsStartupValidator"/>'s fail-loud boot behavior. The
/// validator no-ops when the skill pack is disabled and throws a clear
/// <see cref="InvalidOperationException"/> for each class of misconfiguration
/// when enabled.
/// </summary>
public sealed class GitOpsStartupValidatorTests
{
    private static GitOpsStartupValidator CreateSut(AppConfig appConfig)
        => new(GitOpsTestConfig.Monitor(appConfig), NullLogger<GitOpsStartupValidator>.Instance);

    [Fact]
    public async Task StartAsync_Disabled_NoOps()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.Enabled = false;
        // Even with everything else blanked out, disabled means no validation.
        appConfig.AI.GitOps.ActiveController = "";
        appConfig.AI.GitOps.RemediationRepoUrl = "";
        var sut = CreateSut(appConfig);

        var act = async () => await sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_FullyValidFlux_DoesNotThrow()
    {
        var sut = CreateSut(GitOpsTestConfig.ValidAppConfig("flux"));

        var act = async () => await sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_FullyValidArgoCd_DoesNotThrow()
    {
        var sut = CreateSut(GitOpsTestConfig.ValidAppConfig("argocd"));

        var act = async () => await sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_EmptyActiveController_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.ActiveController = "";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ActiveController*");
    }

    [Fact]
    public async Task StartAsync_UnknownActiveController_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.ActiveController = "spinnaker";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ActiveController*");
    }

    [Fact]
    public async Task StartAsync_K8sGptServerNotRegistered_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.K8sGptMcpServerName = "not-registered";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*K8sGPT*");
    }

    [Fact]
    public async Task StartAsync_EmptyK8sGptServerName_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.K8sGptMcpServerName = "";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_FluxBaseUrlEmpty_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig("flux");
        appConfig.AI.GitOps.FluxApiBaseUrl = "";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FluxApiBaseUrl*");
    }

    [Fact]
    public async Task StartAsync_FluxBaseUrlNotHttp_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig("flux");
        appConfig.AI.GitOps.FluxApiBaseUrl = "ftp://flux.example.com";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FluxApiBaseUrl*");
    }

    [Fact]
    public async Task StartAsync_ArgoCdBaseUrlEmpty_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig("argocd");
        appConfig.AI.GitOps.ArgoCdApiBaseUrl = "";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ArgoCdApiBaseUrl*");
    }

    [Fact]
    public async Task StartAsync_RemediationRepoUrlEmpty_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.RemediationRepoUrl = "";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RemediationRepoUrl*");
    }

    [Fact]
    public async Task StartAsync_RemediationRepoUrlNotAbsolute_Throws()
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.RemediationRepoUrl = "not-a-url";
        var sut = CreateSut(appConfig);

        await FluentActions.Awaiting(() => sut.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RemediationRepoUrl*");
    }

    [Fact]
    public async Task StopAsync_NoOps()
    {
        var sut = CreateSut(GitOpsTestConfig.ValidAppConfig());

        var act = async () => await sut.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
