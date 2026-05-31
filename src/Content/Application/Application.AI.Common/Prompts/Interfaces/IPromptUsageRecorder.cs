using Application.AI.Common.Prompts.Models;
using Domain.AI.Prompts;

namespace Application.AI.Common.Prompts.Interfaces;

/// <summary>
/// Records that a specific <see cref="PromptDescriptor"/> was used at a specific
/// case / span. Used by OTel-stamping recorders (which attach <c>prompt.name</c> +
/// <c>prompt.version</c> + <c>prompt.hash</c> tags to <c>Activity.Current</c>) and
/// by persistence recorders that write to a <c>prompt_usage</c> table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exception contract:</b> implementations MUST NOT throw. Failures must be
/// caught and logged inside the implementation; the caller is on the LLM hot path
/// and observability errors are never worth aborting a request for. The metric/
/// command call sites do NOT defensively wrap calls to this method — bugs in
/// implementations that surface as unhandled exceptions are real defects to fix,
/// not transient conditions to swallow.
/// </para>
/// <para>
/// Cheap to call: every LLM-issuing operation may invoke a recorder per case.
/// </para>
/// </remarks>
public interface IPromptUsageRecorder
{
    /// <summary>
    /// Records that <paramref name="descriptor"/> was used in the context described
    /// by <paramref name="context"/>. Implementations attach to <c>Activity.Current</c>
    /// (when present), persist to storage, or both.
    /// </summary>
    /// <param name="descriptor">The resolved descriptor whose body was sent to the LLM.</param>
    /// <param name="context">Per-invocation attribution context (case id, consuming surface, free-form tags). Use <see cref="PromptUsageContext.Empty"/> when no per-case info is available.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created usage record (for caller diagnostics / chaining).</returns>
    Task<PromptUsageRecord> RecordAsync(
        PromptDescriptor descriptor,
        PromptUsageContext context,
        CancellationToken cancellationToken);
}
