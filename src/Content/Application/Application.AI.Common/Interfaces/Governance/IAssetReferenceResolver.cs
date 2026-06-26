using Domain.AI.Governance;

namespace Application.AI.Common.Interfaces.Governance;

/// <summary>
/// Maps a tool invocation to the external <see cref="AssetReference"/> it reads from or writes to, so the
/// classification gate can resolve that asset's sensitivity before the tool runs. One resolver understands
/// one (or a few) tools' argument shapes; tools no resolver claims fall to the unknown-asset policy.
/// </summary>
/// <remarks>
/// <para>
/// Resolvers are the per-tool adapter layer of the classification gate: only the resolver knows that the
/// <c>file_system</c> tool's <c>path</c> argument is a local file, or that a blob tool's argument is a Data
/// Map qualified name. Implementations are registered as an enumerable and consulted in turn; the first to
/// claim the tool wins, and when none does the gate uses <see cref="AssetReference.Unknown"/>.
/// </para>
/// <para>
/// This is the documented extension point for asset coverage. A consumer adds classification for a new tool
/// by registering a resolver for it — including richer resolution such as reading a local file's embedded
/// Microsoft Information Protection label id (which requires the native MIP File SDK and is therefore left
/// to the consumer) and stamping it onto the returned reference's identifier.
/// </para>
/// </remarks>
public interface IAssetReferenceResolver
{
    /// <summary>
    /// Attempts to resolve the asset a tool call targets from its arguments.
    /// </summary>
    /// <param name="toolName">The tool being invoked.</param>
    /// <param name="arguments">The tool-call arguments the model supplied.</param>
    /// <param name="asset">
    /// The resolved asset reference when this resolver claims the tool; otherwise undefined.
    /// </param>
    /// <returns>
    /// <c>true</c> when this resolver handled the tool and produced an <paramref name="asset"/>;
    /// <c>false</c> when the tool is not one it understands, so the gate should try the next resolver.
    /// </returns>
    bool TryResolve(string toolName, IReadOnlyDictionary<string, object?> arguments, out AssetReference asset);
}
