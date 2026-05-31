using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.AI.Common.Prompts.Interfaces;
using Domain.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Evaluation.Metrics.Rag;

/// <summary>
/// Scores how well the assistant's answer addresses the specific question asked,
/// independent of factual correctness. Requires <see cref="EvalCase.Input"/> and
/// <see cref="AgentInvocationResult.Output"/>.
/// </summary>
public sealed class AnswerRelevanceMetric : RagJudgeMetricBase
{
    /// <inheritdoc />
    public AnswerRelevanceMetric(
        ILlmJudge judge,
        IPromptRegistry promptRegistry,
        IPromptUsageRecorder usageRecorder,
        ILogger<AnswerRelevanceMetric> logger)
        : base(judge, promptRegistry, usageRecorder, logger) { }

    /// <inheritdoc />
    public override string Key => "answer_relevance";

    /// <inheritdoc />
    protected override RagInputs RequiredFields => RagInputs.Input | RagInputs.Output;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> BuildVariables(EvalCase @case, AgentInvocationResult output)
        => new Dictionary<string, string?>
        {
            ["input"] = @case.Input,
            ["output"] = output.Output,
        };
}
