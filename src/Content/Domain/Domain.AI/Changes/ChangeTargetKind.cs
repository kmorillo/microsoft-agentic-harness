namespace Domain.AI.Changes;

/// <summary>
/// Discriminator for the polymorphic <see cref="ChangeTarget"/> hierarchy. The kind
/// determines which concrete subtype the target is and which <c>IChangeApplier</c>
/// the <c>MergeGate</c> resolves at apply time.
/// </summary>
/// <remarks>
/// The set is intentionally open via DI keyed registration of <c>IChangeApplier</c>
/// implementations. Concrete <see cref="ChangeTarget"/> subtypes ship in the Domain
/// for the three first-party targets; consumer-defined targets can subclass
/// <see cref="ChangeTarget"/> and supply their own <see cref="ChangeTargetKind"/>
/// value via the open-string equivalent at the gate-resolver layer (PR-9/10).
/// </remarks>
public enum ChangeTargetKind
{
    /// <summary>The target is unset or unknown. A proposal with this kind cannot be evaluated and is rejected by the validation gate.</summary>
    Unspecified = 0,

    /// <summary>A git repository working copy at a specific branch and head SHA. Diff applied as a commit.</summary>
    GitRepo = 1,

    /// <summary>A Kubernetes resource (Deployment, Service, ConfigMap, etc.) declared in a GitOps repo. Diff written to the GitOps repo; <c>kubectl apply</c> is never invoked directly.</summary>
    KubernetesResource = 2,

    /// <summary>An infrastructure-as-code deployment (Terraform module, Bicep deployment, etc.). Diff applied via the IaC backend's plan/apply cycle.</summary>
    IacDeployment = 3
}
