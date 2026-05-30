using Application.AI.Common.Evaluation.Models;

namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Shared LLM-judge service used by every judge-backed metric (the rubric-driven
/// <c>LlmJudgeMetric</c> plus the 5 RAG metrics: faithfulness, context precision,
/// context recall, answer relevance, answer correctness).
/// </summary>
/// <remarks>
/// <para>
/// Takes a structured <see cref="LlmJudgeRequest"/> so injection mitigations
/// (HtmlEncode of variable values + per-invocation nonce envelope + nonce-collision
/// detection) are applied at this boundary rather than each caller's responsibility.
/// </para>
/// <para>
/// Centralizes the judge call mechanics: chat-client resolution via
/// <see cref="IJudgeChatClientProvider"/>, JSON parsing via
/// <see cref="Json.LlmJsonResponseParser"/>, one stricter retry on malformed output,
/// soft-fail to <see cref="Outcomes.LlmJudgeOutcome.Malformed"/> rather than throwing,
/// token-accumulation and cost computation via <see cref="JudgeCostOptions"/>.
/// </para>
/// </remarks>
public interface ILlmJudge
{
    /// <summary>
    /// Renders the supplied prompt template, applies injection defenses, calls the
    /// configured judge model, and returns the parsed score and reasoning.
    /// Never throws for expected failure modes — see <see cref="LlmJudgeResult.Outcome"/>.
    /// </summary>
    /// <param name="request">Structured request: rubric, template, variables.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A structured result with the parsed score, raw output, and cost.</returns>
    Task<LlmJudgeResult> JudgeAsync(LlmJudgeRequest request, CancellationToken cancellationToken);
}
