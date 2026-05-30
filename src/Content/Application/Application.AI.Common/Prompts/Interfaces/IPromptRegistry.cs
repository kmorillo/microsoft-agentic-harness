using Domain.AI.Prompts;

namespace Application.AI.Common.Prompts.Interfaces;

/// <summary>
/// Resolves versioned prompt templates from the registry. Implementations are typically
/// backed by a folder convention (<c>prompts/{name}/v{version}.md</c>) but may also
/// proxy a remote registry in production deployments.
/// </summary>
/// <remarks>
/// <para>
/// Registry contracts:
/// <list type="bullet">
///   <item><description>Names are case-insensitive, kebab-case by convention.</description></item>
///   <item><description>Versions are case-insensitive ("v1" == "V1" == "1").</description></item>
///   <item><description><see cref="GetLatestAsync"/> returns the descriptor with the highest <see cref="PromptVersion"/>; ties are impossible since (Major, Minor) is unique within a name.</description></item>
///   <item><description>Implementations should cache descriptors and content hashes — templates are immutable per (name, version).</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IPromptRegistry
{
    /// <summary>
    /// Resolves the latest version of the named prompt.
    /// </summary>
    /// <param name="name">Registry name (case-insensitive).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest <see cref="PromptDescriptor"/>.</returns>
    /// <exception cref="KeyNotFoundException">When no version of the prompt exists.</exception>
    Task<PromptDescriptor> GetLatestAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a specific version of the named prompt.
    /// </summary>
    /// <param name="name">Registry name (case-insensitive).</param>
    /// <param name="version">The exact version to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor for the requested version.</returns>
    /// <exception cref="KeyNotFoundException">When the version does not exist for that name.</exception>
    Task<PromptDescriptor> GetAsync(string name, PromptVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates all versions of the named prompt, ascending by <see cref="PromptVersion"/>.
    /// Returns an empty list when no such prompt exists.
    /// </summary>
    Task<IReadOnlyList<PromptDescriptor>> ListAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates the names of every prompt the registry knows about. Order is implementation-defined.
    /// </summary>
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken);
}
