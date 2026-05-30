using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Evaluation.Metrics.Rag;

/// <summary>
/// Scores whether the assistant's answer is grounded in the retrieved context (faithful)
/// versus hallucinated. Requires <see cref="EvalCase.RetrievedContext"/> and
/// <see cref="AgentInvocationResult.Output"/>.
/// </summary>
public sealed class FaithfulnessMetric : RagJudgeMetricBase
{
    /// <inheritdoc />
    public FaithfulnessMetric(
        ILlmJudge judge,
        IPromptTemplateLoader templateLoader,
        ILogger<FaithfulnessMetric> logger)
        : base(judge, templateLoader, logger) { }

    /// <inheritdoc />
    public override string Key => "faithfulness";

    /// <inheritdoc />
    protected override string TemplateName => "faithfulness";

    /// <inheritdoc />
    protected override RagInputs RequiredFields => RagInputs.RetrievedContext | RagInputs.Output;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> BuildVariables(EvalCase @case, AgentInvocationResult output)
        => new Dictionary<string, string?>
        {
            ["retrieved_context"] = @case.RetrievedContext,
            ["output"] = output.Output,
        };
}
