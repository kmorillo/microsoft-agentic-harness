using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// Fail-fast placeholder <see cref="IRolloutRunner"/>. See
/// <see cref="NotConfiguredPatchProposer"/> for the rationale.
/// </summary>
public sealed class NotConfiguredRolloutRunner : IRolloutRunner
{
    /// <inheritdoc />
    public Task<IReadOnlyList<RolloutResult>> RunAsync(
        string skillContent, RolloutBatch batch, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No IRolloutRunner is configured. Replace the default NotConfiguredRolloutRunner " +
            "with an implementation that drives the candidate skill against your eval split " +
            "(e.g. via the existing IEvalRunner / IAgentInvoker stack) before invoking TrainSkillCommand.");
}
