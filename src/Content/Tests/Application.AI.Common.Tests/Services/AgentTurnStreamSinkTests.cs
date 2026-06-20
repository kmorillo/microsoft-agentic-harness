using Application.AI.Common.Services;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services;

/// <summary>
/// Unit tests for <see cref="AgentTurnStreamSink"/> — the ambient, delegate-backed sink
/// the orchestrator attaches so the agent-turn handler streams token deltas to the transport.
/// </summary>
public class AgentTurnStreamSinkTests
{
    [Fact]
    public async Task EmitAsync_ForwardsDelta_ToTheCallback()
    {
        var received = new List<string>();
        var sink = new AgentTurnStreamSink((delta, _) => { received.Add(delta); return Task.CompletedTask; });

        await sink.EmitAsync("Hello ", CancellationToken.None);
        await sink.EmitAsync("world", CancellationToken.None);

        received.Should().Equal("Hello ", "world");
    }

    [Fact]
    public async Task EmitAsync_IgnoresEmptyDelta_WithoutInvokingTheCallback()
    {
        var invoked = false;
        var sink = new AgentTurnStreamSink((_, _) => { invoked = true; return Task.CompletedTask; });

        await sink.EmitAsync("", CancellationToken.None);

        invoked.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullCallback_Throws()
    {
        var act = () => new AgentTurnStreamSink(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Current_IsNullByDefault_AndRoundTrips()
    {
        AgentTurnStreamSink.Current.Should().BeNull();

        var sink = new AgentTurnStreamSink((_, _) => Task.CompletedTask);
        AgentTurnStreamSink.Current = sink;
        try
        {
            AgentTurnStreamSink.Current.Should().BeSameAs(sink);
        }
        finally
        {
            AgentTurnStreamSink.Current = null;
        }
    }
}
