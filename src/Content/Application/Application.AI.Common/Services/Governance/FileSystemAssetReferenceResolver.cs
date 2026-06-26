using Application.AI.Common.Interfaces.Governance;
using Domain.AI.Governance;

namespace Application.AI.Common.Services.Governance;

/// <summary>
/// Resolves the <c>file_system</c> tool's target into a <see cref="AssetType.LocalFile"/> asset reference,
/// using the tool's <c>path</c> argument as the asset identifier.
/// </summary>
/// <remarks>
/// <para>
/// The reference implementation of <see cref="IAssetReferenceResolver"/>. It maps the local path the agent
/// is about to read or write to a <see cref="AssetType.LocalFile"/> reference. It does <em>not</em> read
/// the file's embedded Microsoft Information Protection label — that requires the native MIP File SDK, a
/// heavyweight per-platform dependency intentionally left out of the template. As a result a plain local
/// path carries no embedded label id, so the Information Protection provider resolves it to Unknown (the
/// documented common case) and the unknown-asset policy applies.
/// </para>
/// <para>
/// A consumer who needs local-file label enforcement replaces this resolver with one that extracts the
/// embedded label id (via the MIP File SDK) and stamps it onto <see cref="AssetReference.Identifier"/> in
/// the form the Graph provider understands.
/// </para>
/// </remarks>
public sealed class FileSystemAssetReferenceResolver : IAssetReferenceResolver
{
    /// <summary>The keyed-DI name of the file-system tool this resolver claims.</summary>
    public const string FileSystemToolName = "file_system";

    /// <summary>The file-system tool argument carrying the target path.</summary>
    private const string PathArgument = "path";

    /// <inheritdoc />
    public bool TryResolve(string toolName, IReadOnlyDictionary<string, object?> arguments, out AssetReference asset)
    {
        asset = AssetReference.Unknown();

        if (!string.Equals(toolName, FileSystemToolName, StringComparison.Ordinal))
            return false;

        // The tool claims this call regardless of whether a usable path is present: a file_system call
        // with a missing or blank path targets no resolvable asset, so it resolves to Unknown here rather
        // than falling through to another resolver that also will not understand it.
        if (arguments.TryGetValue(PathArgument, out var value) && value is string path && !string.IsNullOrWhiteSpace(path))
            asset = new AssetReference(AssetType.LocalFile, path.Trim());

        return true;
    }
}
