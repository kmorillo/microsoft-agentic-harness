using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.AI.Common.CQRS.SkillTraining.ReflectOnFailures;

/// <summary>
/// Handles <see cref="ReflectOnFailuresCommand"/> by delegating to <see cref="IPatchProposer"/>.
/// </summary>
/// <remarks>
/// Surfaces proposer failures (null return, exception, cancellation) as
/// <see cref="Result{T}"/> failures so the orchestrator can decide whether to retry,
/// skip the step, or escalate without dealing with exceptions through MediatR.
/// </remarks>
public sealed class ReflectOnFailuresCommandHandler
    : IRequestHandler<ReflectOnFailuresCommand, Result<Patch>>
{
    private readonly IPatchProposer _proposer;
    private readonly ILogger<ReflectOnFailuresCommandHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="ReflectOnFailuresCommandHandler"/> class.</summary>
    public ReflectOnFailuresCommandHandler(
        IPatchProposer proposer,
        ILogger<ReflectOnFailuresCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(proposer);
        ArgumentNullException.ThrowIfNull(logger);
        _proposer = proposer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<Patch>> Handle(
        ReflectOnFailuresCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var input = new ReflectionInput
        {
            CurrentSkill = request.CurrentSkill,
            Rollouts = request.Rollouts,
            MetaSkillMemory = request.MetaSkillMemory,
            IncludeSuccesses = request.IncludeSuccesses
        };

        Patch patch;
        try
        {
            patch = await _proposer.ProposeAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Log the full exception (with all detail) via structured logging, but return a
            // scrubbed stable code in Result.Fail. Raw exception messages from HTTP-backed
            // proposers can include URLs with SAS tokens, header values, or response bodies
            // that should not flow into orchestrator logs / dashboards / AG-UI notifications.
            _logger.LogWarning(ex,
                "Patch proposer threw while reflecting on {Count} rollouts; treating step as no-op.",
                request.Rollouts.Count);
            return Result<Patch>.Fail(ProposerCallFailedCode);
        }

        if (patch is null)
        {
            return Result<Patch>.Fail(ProposerReturnedNullCode);
        }

        return Result<Patch>.Success(patch);
    }

    /// <summary>Stable failure code emitted when the proposer threw. Full detail lives in the logger.</summary>
    public const string ProposerCallFailedCode = "skill_training.reflect.proposer_call_failed";

    /// <summary>Stable failure code emitted when the proposer returned a null patch.</summary>
    public const string ProposerReturnedNullCode = "skill_training.reflect.proposer_returned_null";
}
