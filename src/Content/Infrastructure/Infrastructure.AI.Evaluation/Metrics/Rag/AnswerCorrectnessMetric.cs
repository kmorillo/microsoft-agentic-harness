using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.AI.Common.Prompts.Interfaces;
using Domain.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Evaluation.Metrics.Rag;

/// <summary>
/// Scores how closely the assistant's answer agrees with the reference answer,
/// semantically and factually. Requires <see cref="EvalCase.ExpectedOutput"/> and
/// <see cref="AgentInvocationResult.Output"/>.
/// </summary>
public sealed class AnswerCorrectnessMetric : RagJudgeMetricBase
{
    /// <inheritdoc />
    public AnswerCorrectnessMetric(
        ILlmJudge judge,
        IPromptRegistry promptRegistry,
        IPromptUsageRecorder usageRecorder,
        ILogger<AnswerCorrectnessMetric> logger)
        : base(judge, promptRegistry, usageRecorder, logger) { }

    /// <inheritdoc />
    public override string Key => "answer_correctness";

    /// <inheritdoc />
    protected override RagInputs RequiredFields => RagInputs.ExpectedOutput | RagInputs.Output;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> BuildVariables(EvalCase @case, AgentInvocationResult output)
        => new Dictionary<string, string?>
        {
            ["expected_output"] = @case.ExpectedOutput,
            ["output"] = output.Output,
        };
}
