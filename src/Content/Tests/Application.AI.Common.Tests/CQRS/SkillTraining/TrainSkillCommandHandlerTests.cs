using Application.AI.Common.CQRS.SkillTraining.TrainSkill;
using Application.AI.Common.Interfaces.SkillTraining;
using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.AI.Common.Tests.CQRS.SkillTraining;

public class TrainSkillCommandHandlerTests
{
    private static readonly PatchApplier Applier = new();
    private static readonly PatchAggregator Aggregator = new();
    private static readonly TopKEditSelector Selector = new();
    private static readonly GateEvaluator Gate = new();

    private static TrainSkillCommandHandler NewSut(
        IRolloutRunner runner,
        IPatchProposer proposer,
        InMemorySkillTrainingCheckpointStore store)
    {
        return new TrainSkillCommandHandler(
            runner, proposer, Aggregator, Selector, Applier, Gate,
            store, new NoOpMediator(), TimeProvider.System,
            NullLogger<TrainSkillCommandHandler>.Instance);
    }

    private static TrainSkillCommand NewCommand(TrainSkillConfig config) => new()
    {
        RunId = "run-1",
        SkillId = "skill-X",
        InitialSkill = "# initial\n- baseline rule",
        Config = config
    };

    [Fact]
    public async Task Handle_AcceptedCandidate_UpdatesStateAndPersistsCheckpoint()
    {
        // Stub runner: train rollouts fail (so the optimizer has reason to propose); val rollouts pass (so gate accepts).
        var runner = new StubRolloutRunner((skill, batch) => batch.Split switch
        {
            "train" => [new RolloutResult { ItemId = "t1", Hard = 0.0, Soft = 0.2 }],
            "val" => [new RolloutResult { ItemId = "v1", Hard = 1.0, Soft = 1.0 }],
            _ => []
        });
        var proposer = new StubProposer(input => new Patch
        {
            Edits = [new Edit { Op = EditOp.Append, Content = "- new rule" }]
        });
        var store = new InMemorySkillTrainingCheckpointStore();
        var sut = NewSut(runner, proposer, store);

        var result = await sut.Handle(
            NewCommand(new TrainSkillConfig
            {
                Epochs = 1, StepsPerEpoch = 1, LrStart = 4, LrMin = 1,
                LrScheduler = "constant", Patience = 6,
                UseSlowUpdate = false, UseMetaSkill = false
            }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var run = result.Value!;
        run.StepsExecuted.Should().Be(1);
        run.BestScore.Should().Be(1.0);
        run.Steps.Should().HaveCount(1);
        run.Steps[0].Action.Should().Be(GateAction.AcceptNewBest);
        run.Steps[0].AppliedEditCount.Should().Be(1);
        run.BestSkill.Should().Contain("new rule");

        var checkpoint = await store.GetAsync("run-1", 1, default);
        checkpoint!.Action.Should().Be(GateAction.AcceptNewBest);
        checkpoint.SkillHash.Should().HaveLength(64, because: "SHA-256 lowercase-hex is 64 chars");
    }

    [Fact]
    public async Task Handle_AllRejected_EarlyStopOnPatience()
    {
        // Val always returns failure → every candidate is Rejected.
        var runner = new StubRolloutRunner((skill, batch) =>
            [new RolloutResult { ItemId = "x", Hard = 0.0, Soft = 0.0 }]);
        // Proposer always proposes a real edit so we reach the gate path each time.
        var proposer = new StubProposer(_ => new Patch
        {
            Edits = [new Edit { Op = EditOp.Append, Content = "- another rule" }]
        });
        var sut = NewSut(runner, proposer, new InMemorySkillTrainingCheckpointStore());

        var result = await sut.Handle(
            NewCommand(new TrainSkillConfig
            {
                Epochs = 2, StepsPerEpoch = 5, LrStart = 4, LrMin = 1,
                LrScheduler = "constant", Patience = 2,
                UseSlowUpdate = false, UseMetaSkill = false
            }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StepsExecuted.Should().Be(2, because: "patience=2 stops after 2 rejects");
        result.Value.ConsecutiveRejects.Should().Be(2);
        result.Value.Steps.Should().AllSatisfy(s => s.Action.Should().Be(GateAction.Reject));
    }

    [Fact]
    public async Task Handle_ProposerProposesNoEdits_RecordsRejectNoOp_Continues()
    {
        var runner = new StubRolloutRunner((_, _) =>
            [new RolloutResult { ItemId = "x", Hard = 1.0, Soft = 1.0 }]);
        var proposer = new StubProposer(_ => new Patch());  // zero edits

        var sut = NewSut(runner, proposer, new InMemorySkillTrainingCheckpointStore());

        var result = await sut.Handle(
            NewCommand(new TrainSkillConfig
            {
                Epochs = 1, StepsPerEpoch = 3, LrStart = 4, LrMin = 1,
                LrScheduler = "constant", Patience = 10,
                UseSlowUpdate = false, UseMetaSkill = false
            }),
            CancellationToken.None);

        result.Value!.Steps.Should().AllSatisfy(s =>
        {
            s.Action.Should().Be(GateAction.Reject);
            s.AppliedEditCount.Should().Be(0);
        });
        result.Value.BestScore.Should().Be(0.0, because: "no candidate was ever accepted");
    }

    [Fact]
    public async Task Handle_ProposerThrows_RecordsRejectAndContinues_UnlessPatience()
    {
        var runner = new StubRolloutRunner((_, _) =>
            [new RolloutResult { ItemId = "x", Hard = 1.0, Soft = 1.0 }]);
        var proposer = new StubProposer(_ => throw new InvalidOperationException("boom"));
        var sut = NewSut(runner, proposer, new InMemorySkillTrainingCheckpointStore());

        var result = await sut.Handle(
            NewCommand(new TrainSkillConfig
            {
                Epochs = 1, StepsPerEpoch = 5, LrStart = 4, LrMin = 1,
                LrScheduler = "constant", Patience = 3,
                UseSlowUpdate = false, UseMetaSkill = false
            }),
            CancellationToken.None);

        // 3 thrown reflects = 3 rejects = patience exhausted.
        result.Value!.StepsExecuted.Should().Be(3);
        result.Value.Steps.Should().AllSatisfy(s => s.Action.Should().Be(GateAction.Reject));
    }

    [Fact]
    public async Task Handle_Cancelled_PropagatesOperationCanceled()
    {
        var runner = new StubRolloutRunner((_, _) => []);
        var proposer = new StubProposer(_ => new Patch());
        var sut = NewSut(runner, proposer, new InMemorySkillTrainingCheckpointStore());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.Handle(NewCommand(new TrainSkillConfig
        {
            Epochs = 1, StepsPerEpoch = 1, LrStart = 4, LrMin = 1,
            LrScheduler = "constant", Patience = 1,
            UseSlowUpdate = false, UseMetaSkill = false
        }), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Stubs ────────────────────────────────────────────────────────────────────

    private sealed class StubRolloutRunner : IRolloutRunner
    {
        private readonly Func<string, RolloutBatch, IReadOnlyList<RolloutResult>> _fn;
        public StubRolloutRunner(Func<string, RolloutBatch, IReadOnlyList<RolloutResult>> fn) => _fn = fn;
        public Task<IReadOnlyList<RolloutResult>> RunAsync(string skillContent, RolloutBatch batch, CancellationToken ct)
            => Task.FromResult(_fn(skillContent, batch));
    }

    private sealed class StubProposer : IPatchProposer
    {
        private readonly Func<ReflectionInput, Patch> _fn;
        public StubProposer(Func<ReflectionInput, Patch> fn) => _fn = fn;
        public Task<Patch> ProposeAsync(ReflectionInput input, CancellationToken cancellationToken)
            => Task.FromResult(_fn(input));
    }

    private sealed class NoOpMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResponse)!);
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
            => Task.CompletedTask;
    }
}
