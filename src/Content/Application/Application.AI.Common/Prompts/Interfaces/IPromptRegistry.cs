using Application.AI.Common.Prompts.Exceptions;
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
/// <para>
/// <b>Exception contract.</b> Implementations of this interface MUST throw only:
/// <list type="bullet">
///   <item><description><see cref="KeyNotFoundException"/> when the prompt name (or version) is unknown to the registry — a "the resource doesn't exist" semantic.</description></item>
///   <item><description><see cref="PromptRegistryUnavailableException"/> when the resource may exist but cannot be retrieved (transient IO, malformed body, backend unreachable). The original backend exception is wrapped as <see cref="Exception.InnerException"/>.</description></item>
///   <item><description><see cref="OperationCanceledException"/> when the supplied cancellation token fires.</description></item>
/// </list>
/// Any other exception escaping a registry implementation is a contract violation —
/// consumers may safely treat it as a defect, not a runtime condition.
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
    /// <exception cref="PromptRegistryUnavailableException">When the backend cannot serve the request (transient or malformed).</exception>
    Task<PromptDescriptor> GetLatestAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a specific version of the named prompt.
    /// </summary>
    /// <param name="name">Registry name (case-insensitive).</param>
    /// <param name="version">The exact version to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor for the requested version.</returns>
    /// <exception cref="KeyNotFoundException">When the version does not exist for that name.</exception>
    /// <exception cref="PromptRegistryUnavailableException">When the backend cannot serve the request (transient or malformed).</exception>
    Task<PromptDescriptor> GetAsync(string name, PromptVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates all versions of the named prompt, ascending by <see cref="PromptVersion"/>.
    /// Returns an empty list when no such prompt exists.
    /// </summary>
    /// <exception cref="PromptRegistryUnavailableException">When the backend cannot serve the request (transient or malformed).</exception>
    Task<IReadOnlyList<PromptDescriptor>> ListAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates the names of every prompt the registry knows about. Order is implementation-defined.
    /// </summary>
    /// <exception cref="PromptRegistryUnavailableException">When the backend cannot serve the request (transient or malformed).</exception>
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken);
}
