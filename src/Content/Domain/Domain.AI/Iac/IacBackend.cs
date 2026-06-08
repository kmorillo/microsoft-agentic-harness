namespace Domain.AI.Iac;

/// <summary>
/// The infrastructure-as-code backend a generation / plan / scan operation
/// targets. Each value maps to a keyed <c>IIacGenerator</c> registration and to
/// the <c>backend</c> string carried on an <c>IacDeploymentTarget</c>.
/// </summary>
/// <remarks>
/// The harness ships Terraform and Bicep with parity per the PR-10 plan — the
/// template must not implicitly cloud-lock by shipping Bicep alone. Consumers add
/// a third backend (e.g. Pulumi, CloudFormation) by implementing
/// <c>IIacGenerator</c> and registering it under a new key; this enum is the
/// first-party set, not a closed universe.
/// </remarks>
public enum IacBackend
{
    /// <summary>HashiCorp Terraform (<c>terraform validate</c> / <c>plan</c>; Checkov + tfsec scanners).</summary>
    Terraform = 0,

    /// <summary>Azure Bicep (<c>bicep build</c>; ARM-TTK + Checkov scanners).</summary>
    Bicep = 1
}

/// <summary>
/// Maps <see cref="IacBackend"/> values to and from their canonical lowercase
/// string keys — the keys used for keyed-DI resolution and on
/// <c>IacDeploymentTarget.Backend</c>.
/// </summary>
public static class IacBackendKeys
{
    /// <summary>Canonical key for <see cref="IacBackend.Terraform"/>.</summary>
    public const string Terraform = "terraform";

    /// <summary>Canonical key for <see cref="IacBackend.Bicep"/>.</summary>
    public const string Bicep = "bicep";

    /// <summary>Returns the canonical lowercase key for a backend value.</summary>
    /// <param name="backend">The backend to convert.</param>
    /// <returns>The canonical key string.</returns>
    public static string ToKey(this IacBackend backend) => backend switch
    {
        IacBackend.Terraform => Terraform,
        IacBackend.Bicep => Bicep,
        _ => backend.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// Parses a backend key (case-insensitive) to an <see cref="IacBackend"/>.
    /// </summary>
    /// <param name="key">The backend key, e.g. <c>"terraform"</c> or <c>"bicep"</c>.</param>
    /// <param name="backend">The parsed backend when the key is recognised.</param>
    /// <returns><see langword="true"/> when the key maps to a known backend; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? key, out IacBackend backend)
    {
        switch (key?.Trim().ToLowerInvariant())
        {
            case Terraform:
                backend = IacBackend.Terraform;
                return true;
            case Bicep:
                backend = IacBackend.Bicep;
                return true;
            default:
                backend = default;
                return false;
        }
    }
}
