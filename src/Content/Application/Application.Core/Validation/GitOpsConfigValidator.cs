using Domain.Common.Config.AI.GitOps;
using FluentValidation;

namespace Application.Core.Validation;

/// <summary>
/// Validates <see cref="GitOpsConfig"/>. All rules are conditional on
/// <see cref="GitOpsConfig.Enabled"/> — a disabled skill pack imposes no
/// constraints so the template runs out of the box. When enabled the rules
/// mirror <c>GitOpsStartupValidator</c> so a misconfiguration is caught both by
/// the options-validation pipeline and at host boot.
/// </summary>
public sealed class GitOpsConfigValidator : AbstractValidator<GitOpsConfig>
{
    private static readonly string[] KnownControllers = ["flux", "argocd"];

    /// <summary>Initializes a new instance of the <see cref="GitOpsConfigValidator"/> class.</summary>
    public GitOpsConfigValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.ActiveController)
                .Must(c => KnownControllers.Contains(c?.Trim().ToLowerInvariant()))
                .WithMessage("ActiveController must be \"flux\" or \"argocd\" when GitOps is enabled.");

            RuleFor(x => x.K8sGptMcpServerName)
                .NotEmpty()
                .WithMessage("K8sGptMcpServerName is required when GitOps is enabled (K8sGPT is not gracefully degraded).");

            RuleFor(x => x.RemediationRepoUrl)
                .NotEmpty()
                .Must(BeAbsoluteUrl)
                .WithMessage("RemediationRepoUrl must be a non-empty absolute URL when GitOps is enabled.");

            When(x => string.Equals(x.ActiveController?.Trim(), "flux", StringComparison.OrdinalIgnoreCase), () =>
                RuleFor(x => x.FluxApiBaseUrl)
                    .Must(BeAbsoluteHttpUrl)
                    .WithMessage("FluxApiBaseUrl must be a valid absolute http(s) URL when ActiveController is \"flux\"."));

            When(x => string.Equals(x.ActiveController?.Trim(), "argocd", StringComparison.OrdinalIgnoreCase), () =>
                RuleFor(x => x.ArgoCdApiBaseUrl)
                    .Must(BeAbsoluteHttpUrl)
                    .WithMessage("ArgoCdApiBaseUrl must be a valid absolute http(s) URL when ActiveController is \"argocd\"."));
        });
    }

    private static bool BeAbsoluteUrl(string url)
        => !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out _);

    private static bool BeAbsoluteHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
