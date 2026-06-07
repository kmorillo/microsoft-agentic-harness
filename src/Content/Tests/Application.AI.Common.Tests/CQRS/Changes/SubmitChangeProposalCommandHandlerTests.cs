using Application.AI.Common.CQRS.Changes.SubmitChangeProposal;
using Application.AI.Common.Tests.CQRS.Changes.Support;
using Domain.AI.Changes;
using Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.AI.Common.Tests.CQRS.Changes;

/// <summary>Handler tests for <see cref="SubmitChangeProposalCommandHandler"/>.</summary>
public sealed class SubmitChangeProposalCommandHandlerTests
{
    private static SubmitChangeProposalCommandHandler NewSut(
        InMemoryChangeProposalStore store,
        TestHelpers.StubAgentContext context,
        TestHelpers.StubGateResolver? resolver = null,
        TimeProvider? time = null,
        Microsoft.Extensions.Options.IOptionsMonitor<Domain.Common.Config.AppConfig>? config = null,
        TestHelpers.StubDispatchQueue? dispatchQueue = null) =>
        new(
            store,
            resolver ?? new TestHelpers.StubGateResolver(),
            dispatchQueue ?? new TestHelpers.StubDispatchQueue(),
            context,
            config ?? TestHelpers.EnabledConfigMonitor(),
            time ?? TimeProvider.System,
            NullLogger<SubmitChangeProposalCommandHandler>.Instance);

    private static SubmitChangeProposalCommand DefaultCommand() => new()
    {
        Target = TestHelpers.DefaultTarget(),
        Diff = TestHelpers.DefaultDiff(),
        Summary = "rename foo to bar",
        BlastRadius = BlastRadius.Low
    };

    [Fact]
    public async Task Handle_HappyPath_PersistsDraftAndReturnsProposalAndEnqueues()
    {
        var store = new InMemoryChangeProposalStore();
        var context = new TestHelpers.StubAgentContext(TestHelpers.DefaultIdentity);
        var dispatcher = new TestHelpers.StubDispatchQueue();
        var sut = NewSut(store, context, dispatchQueue: dispatcher);

        var result = await sut.Handle(DefaultCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Status is Draft — handler no longer drives the orchestrator inline;
        // the BackgroundService picks it up out-of-band.
        result.Value!.Status.Should().Be(ChangeProposalStatus.Draft);
        result.Value.SubmittedBy.Should().BeSameAs(TestHelpers.DefaultIdentity);
        result.Value.RequiredGates.Should().Equal("self_validation", "approval", "merge");
        (await store.GetAsync(result.Value.Id, CancellationToken.None)).Should().NotBeNull();
        // Side-effect guard: the proposal was handed off to the worker.
        dispatcher.Enqueued.Should().ContainSingle().Which.Should().Be(result.Value.Id);
    }

    [Fact]
    public async Task Handle_DuplicateWithinIdBucket_ReturnsExistingProposalWithoutReEnqueueing()
    {
        var store = new InMemoryChangeProposalStore();
        var context = new TestHelpers.StubAgentContext(TestHelpers.DefaultIdentity);
        var dispatcher = new TestHelpers.StubDispatchQueue();
        var sut = NewSut(store, context, dispatchQueue: dispatcher);

        var first = await sut.Handle(DefaultCommand(), CancellationToken.None);
        var second = await sut.Handle(DefaultCommand(), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value!.Id.Should().Be(first.Value!.Id);
        second.Value.Should().BeSameAs(first.Value);
        // Only the first submission enqueued — duplicates short-circuit
        // before reaching the dispatcher.
        dispatcher.Enqueued.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_NoAmbientIdentity_ReturnsUnauthorizedAndDoesNotEnqueue()
    {
        var store = new InMemoryChangeProposalStore();
        var context = new TestHelpers.StubAgentContext(identity: null);
        var dispatcher = new TestHelpers.StubDispatchQueue();
        var sut = NewSut(store, context, dispatchQueue: dispatcher);

        var result = await sut.Handle(DefaultCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(ResultFailureType.Unauthorized);
        dispatcher.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ResolverReturnsEmpty_ReturnsFailureAndDoesNotEnqueue()
    {
        var store = new InMemoryChangeProposalStore();
        var context = new TestHelpers.StubAgentContext(TestHelpers.DefaultIdentity);
        var emptyResolver = new TestHelpers.StubGateResolver { ResolvedGates = [] };
        var dispatcher = new TestHelpers.StubDispatchQueue();
        var sut = NewSut(store, context, emptyResolver, dispatchQueue: dispatcher);

        var result = await sut.Handle(DefaultCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("empty gate list");
        dispatcher.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ExplicitRequiredGates_BypassesResolver()
    {
        var store = new InMemoryChangeProposalStore();
        var context = new TestHelpers.StubAgentContext(TestHelpers.DefaultIdentity);
        var sut = NewSut(store, context);

        var cmd = DefaultCommand() with { RequiredGates = new[] { "custom_gate" } };
        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiredGates.Should().Equal("custom_gate");
    }

    [Fact]
    public async Task Handle_PipelineDisabled_ReturnsForbiddenAndDoesNotEnqueue()
    {
        // The pipeline master switch is the first guard in Submit; a disabled
        // pipeline must reject before doing anything (no Save, no Enqueue).
        var store = new InMemoryChangeProposalStore();
        var context = new TestHelpers.StubAgentContext(TestHelpers.DefaultIdentity);
        var dispatcher = new TestHelpers.StubDispatchQueue();
        var sut = NewSut(store, context, config: TestHelpers.DisabledConfigMonitor(), dispatchQueue: dispatcher);

        var result = await sut.Handle(DefaultCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(ResultFailureType.Forbidden);
        result.Errors.Should().ContainSingle().Which.Should().Contain("disabled");
        store.Count.Should().Be(0);
        dispatcher.Enqueued.Should().BeEmpty();
    }
}
