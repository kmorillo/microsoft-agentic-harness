using Application.AI.Common.Prompts.Models;
using Domain.AI.Prompts;

namespace Application.AI.Common.Prompts.Interfaces;

/// <summary>
/// Records that a specific <see cref="PromptDescriptor"/> was used at a specific
/// trace/span. Used by OTel-stamping recorders (which attach <c>prompt.name</c> +
/// <c>prompt.version</c> + <c>prompt.hash</c> tags to <c>Activity.Current</c>) and
/// by persistence recorders that write to a <c>prompt_usage</c> table.
/// </summary>
/// <remarks>
/// Implementations should be cheap to call on the hot path — every LLM-issuing
/// MediatR command may invoke a recorder. Failures must NOT propagate to the
/// caller; recording is observability, not correctness.
/// </remarks>
public interface IPromptUsageRecorder
{
    /// <summary>
    /// Records that <paramref name="descriptor"/> was used. Implementations attach
    /// to <c>Activity.Current</c> (when present), persist to storage, or both.
    /// Never throws.
    /// </summary>
    /// <param name="descriptor">The resolved descriptor whose body was sent to the LLM.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created usage record (for caller diagnostics / chaining).</returns>
    Task<PromptUsageRecord> RecordAsync(
        PromptDescriptor descriptor,
        CancellationToken cancellationToken);
}
