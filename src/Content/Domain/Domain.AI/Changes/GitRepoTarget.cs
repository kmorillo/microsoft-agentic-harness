namespace Domain.AI.Changes;

/// <summary>
/// A <see cref="ChangeTarget"/> identifying a git repository at a specific branch and
/// optionally a specific head commit. The <c>MergeGate</c> applies the diff by writing
/// a commit on top of <see cref="Branch"/> (and pushing if the applier is configured to).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HeadSha"/> is optional but recommended. When provided, the merge applier
/// can verify the branch head has not moved since the proposal was submitted; if the
/// branch has advanced, the applier should refuse to apply (the proposal becomes stale
/// and must be resubmitted against the new head).
/// </para>
/// <para>
/// <see cref="WorkingPath"/> identifies the path within the repo the diff targets.
/// Empty means the diff may touch any path; a non-empty value restricts the diff to
/// paths under it (validators enforce, not the target itself).
/// </para>
/// </remarks>
public sealed class GitRepoTarget : ChangeTarget
{
    /// <summary>
    /// Construct a <see cref="GitRepoTarget"/>.
    /// </summary>
    /// <param name="repoUrl">The git repository remote URL (https or ssh). Surfaces verbatim in audit lines.</param>
    /// <param name="branch">The branch the diff will be applied to. Required and non-empty.</param>
    /// <param name="headSha">Optional: the head commit SHA the diff was authored against. Null when the proposal does not pin a head.</param>
    /// <param name="workingPath">Optional: the path within the repo the diff is restricted to. Empty when unrestricted.</param>
    public GitRepoTarget(string repoUrl, string branch, string? headSha = null, string workingPath = "")
        : base(ChangeTargetKind.GitRepo, BuildDisplayName(repoUrl, branch))
    {
        RepoUrl = repoUrl ?? string.Empty;
        Branch = branch ?? string.Empty;
        HeadSha = headSha;
        WorkingPath = workingPath ?? string.Empty;
    }

    /// <summary>The git remote URL the diff will be applied to.</summary>
    public string RepoUrl { get; }

    /// <summary>The branch the diff will be applied to.</summary>
    public string Branch { get; }

    /// <summary>The head commit SHA at proposal-submission time. Null when not pinned.</summary>
    public string? HeadSha { get; }

    /// <summary>The path within the repo the diff is restricted to. Empty when unrestricted.</summary>
    public string WorkingPath { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Canonical form: <c>git:{repoUrl}#{branch}@{headSha|HEAD}:{workingPath}</c>.
    /// Two targets that mean the same git location produce the same key; pinning a head
    /// SHA distinguishes proposals authored against different commits of the same branch.
    /// </remarks>
    public override string CanonicalKey() =>
        $"git:{RepoUrl}#{Branch}@{HeadSha ?? "HEAD"}:{WorkingPath}";

    private static string BuildDisplayName(string repoUrl, string branch) =>
        string.IsNullOrEmpty(repoUrl) || string.IsNullOrEmpty(branch)
            ? "(unspecified git target)"
            : $"{repoUrl}#{branch}";
}
