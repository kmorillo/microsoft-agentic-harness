using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;
using Domain.Common;
using MediatR;

namespace Application.AI.Common.CQRS.SkillTraining.GateCandidateSkill;

/// <summary>
/// Handles <see cref="GateCandidateSkillCommand"/> by delegating to <see cref="IGateEvaluator"/>.
/// </summary>
/// <remarks>
/// Thin wrapper — the gate is pure, so the handler exists primarily to attach the gate
/// to the MediatR pipeline (validation, audit, telemetry). All real logic lives in
/// <see cref="IGateEvaluator"/>.
/// </remarks>
public sealed class GateCandidateSkillCommandHandler
    : IRequestHandler<GateCandidateSkillCommand, Result<GateResult>>
{
    private readonly IGateEvaluator _gate;

    /// <summary>Initializes a new instance of the <see cref="GateCandidateSkillCommandHandler"/> class.</summary>
    /// <param name="gate">The pure gate evaluator.</param>
    public GateCandidateSkillCommandHandler(IGateEvaluator gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        _gate = gate;
    }

    /// <inheritdoc />
    public Task<Result<GateResult>> Handle(
        GateCandidateSkillCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var result = _gate.Evaluate(
            candidateSkill: request.CandidateSkill,
            candidateHard: request.CandidateHard,
            candidateSoft: request.CandidateSoft,
            currentSkill: request.CurrentSkill,
            currentScore: request.CurrentScore,
            bestSkill: request.BestSkill,
            bestScore: request.BestScore,
            bestStep: request.BestStep,
            globalStep: request.GlobalStep,
            metric: request.Metric,
            mixedWeight: request.MixedWeight);

        return Task.FromResult(Result<GateResult>.Success(result));
    }
}
