using Application.Core.Validation;
using Domain.Common.Config.AI.GitOps;
using FluentAssertions;
using Xunit;

namespace Application.Core.Tests.Validation;

/// <summary>
/// Tests for <see cref="GitOpsConfigValidator"/>. All rules are conditional on
/// <see cref="GitOpsConfig.Enabled"/>: a disabled skill pack is always valid; an
/// enabled one mirrors the boot-time checks in <c>GitOpsStartupValidator</c>.
/// Pattern: CreateValidConfig() baseline, mutate one field per test.
/// </summary>
public class GitOpsConfigValidatorTests
{
    private readonly GitOpsConfigValidator _validator = new();

    [Fact]
    public void DefaultValues_MatchSpec()
    {
        var config = new GitOpsConfig();

        config.Enabled.Should().BeFalse();
        config.ActiveController.Should().BeEmpty();
        config.K8sGptMcpServerName.Should().Be("k8sgpt");
        config.RemediationBranch.Should().Be("main");
    }

    [Fact]
    public async Task Validate_Disabled_AlwaysValid()
    {
        // Everything is blank/invalid, but Enabled=false short-circuits all rules.
        var config = new GitOpsConfig
        {
            Enabled = false,
            ActiveController = "nonsense",
            K8sGptMcpServerName = "",
            RemediationRepoUrl = "",
            FluxApiBaseUrl = "ftp://bad",
            ArgoCdApiBaseUrl = ""
        };

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ValidFluxConfig_NoErrors()
    {
        var result = await _validator.ValidateAsync(CreateValidConfig("flux"));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ValidArgoCdConfig_NoErrors()
    {
        var result = await _validator.ValidateAsync(CreateValidConfig("argocd"));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("spinnaker")]
    [InlineData("FLUXX")]
    public async Task Validate_UnknownActiveController_HasError(string controller)
    {
        var config = CreateValidConfig("flux");
        config.ActiveController = controller;

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ActiveController");
    }

    [Theory]
    [InlineData("flux")]
    [InlineData("FLUX")]
    [InlineData("argocd")]
    [InlineData(" argocd ")]
    public async Task Validate_KnownActiveControllerCaseAndWhitespaceInsensitive_NoActiveControllerError(string controller)
    {
        var config = CreateValidConfig("flux");
        config.ActiveController = controller;

        var result = await _validator.ValidateAsync(config);

        result.Errors.Should().NotContain(e => e.PropertyName == "ActiveController");
    }

    [Fact]
    public async Task Validate_EmptyK8sGptServerName_HasError()
    {
        var config = CreateValidConfig("flux");
        config.K8sGptMcpServerName = "";

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "K8sGptMcpServerName");
    }

    [Fact]
    public async Task Validate_EmptyRemediationRepoUrl_HasError()
    {
        var config = CreateValidConfig("flux");
        config.RemediationRepoUrl = "";

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RemediationRepoUrl");
    }

    [Fact]
    public async Task Validate_RelativeRemediationRepoUrl_HasError()
    {
        var config = CreateValidConfig("flux");
        config.RemediationRepoUrl = "not-a-url";

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RemediationRepoUrl");
    }

    [Fact]
    public async Task Validate_FluxActiveButFluxUrlNotHttp_HasError()
    {
        var config = CreateValidConfig("flux");
        config.FluxApiBaseUrl = "ftp://flux.example.com";

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FluxApiBaseUrl");
    }

    [Fact]
    public async Task Validate_FluxActiveButFluxUrlEmpty_HasError()
    {
        var config = CreateValidConfig("flux");
        config.FluxApiBaseUrl = "";

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FluxApiBaseUrl");
    }

    [Fact]
    public async Task Validate_ArgoCdActiveButArgoCdUrlNotHttp_HasError()
    {
        var config = CreateValidConfig("argocd");
        config.ArgoCdApiBaseUrl = "ftp://argocd.example.com";

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ArgoCdApiBaseUrl");
    }

    [Fact]
    public async Task Validate_FluxActive_DoesNotValidateArgoCdUrl()
    {
        // ArgoCd URL is junk, but the active controller is flux so it is not checked.
        var config = CreateValidConfig("flux");
        config.ArgoCdApiBaseUrl = "junk";

        var result = await _validator.ValidateAsync(config);

        result.IsValid.Should().BeTrue();
    }

    private static GitOpsConfig CreateValidConfig(string activeController) => new()
    {
        Enabled = true,
        ActiveController = activeController,
        K8sGptMcpServerName = "k8sgpt",
        FluxApiBaseUrl = "https://flux.example.com",
        ArgoCdApiBaseUrl = "https://argocd.example.com",
        RemediationRepoUrl = "https://github.com/example/cluster-config.git",
        RemediationBranch = "main"
    };
}
