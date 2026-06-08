namespace Domain.AI.Iac;

/// <summary>
/// The output of scaffolding an IaC module — the generated files keyed by their
/// relative path. The agent typically submits these as the edits of a
/// <c>ChangeProposal</c> (a <c>GitRepoTarget</c>); the scaffold never writes to
/// disk or the cluster itself.
/// </summary>
public sealed record IacGenerationResult
{
    /// <summary>The backend the files were scaffolded for.</summary>
    public required IacBackend Backend { get; init; }

    /// <summary>
    /// The generated files, keyed by relative path (e.g. <c>main.tf</c>,
    /// <c>variables.tf</c>, <c>main.bicep</c>) to file content.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Files { get; init; }
}
