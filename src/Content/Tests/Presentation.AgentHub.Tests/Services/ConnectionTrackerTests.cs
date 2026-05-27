using FluentAssertions;
using Presentation.AgentHub.DTOs;
using Presentation.AgentHub.Services;
using Xunit;

namespace Presentation.AgentHub.Tests.Services;

/// <summary>
/// Tests for <see cref="ConnectionTracker"/> covering thread-safe track/untrack/enumerate.
/// </summary>
public class ConnectionTrackerTests
{
    [Fact]
    public void Track_Then_Get_ReturnsTrackedInfo()
    {
        var tracker = new ConnectionTracker();
        var info = new ActiveConversationInfo("c1", "agent", "user1", DateTimeOffset.UtcNow, 0, Guid.NewGuid());

        tracker.Track("conn1", info);

        tracker.Get("conn1").Should().Be(info);
    }

    [Fact]
    public void Get_Untracked_ReturnsNull()
    {
        var tracker = new ConnectionTracker();
        tracker.Get("unknown").Should().BeNull();
    }

    [Fact]
    public void Untrack_ReturnsInfoAndRemoves()
    {
        var tracker = new ConnectionTracker();
        var info = new ActiveConversationInfo("c1", "agent", "user1", DateTimeOffset.UtcNow, 0, Guid.NewGuid());
        tracker.Track("conn1", info);

        var removed = tracker.Untrack("conn1");

        removed.Should().Be(info);
        tracker.Get("conn1").Should().BeNull();
    }

    [Fact]
    public void Untrack_Untracked_ReturnsNull()
    {
        var tracker = new ConnectionTracker();
        tracker.Untrack("unknown").Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllTrackedConnections()
    {
        var tracker = new ConnectionTracker();
        var info1 = new ActiveConversationInfo("c1", "agent", "user1", DateTimeOffset.UtcNow, 0, Guid.NewGuid());
        var info2 = new ActiveConversationInfo("c2", "agent", "user2", DateTimeOffset.UtcNow, 0, Guid.NewGuid());
        tracker.Track("conn1", info1);
        tracker.Track("conn2", info2);

        tracker.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Track_SameKey_OverwritesPrevious()
    {
        var tracker = new ConnectionTracker();
        var info1 = new ActiveConversationInfo("c1", "agent", "user1", DateTimeOffset.UtcNow, 0, Guid.NewGuid());
        var info2 = new ActiveConversationInfo("c2", "agent", "user1", DateTimeOffset.UtcNow, 5, Guid.NewGuid());
        tracker.Track("conn1", info1);
        tracker.Track("conn1", info2);

        tracker.Get("conn1").Should().Be(info2);
    }
}
