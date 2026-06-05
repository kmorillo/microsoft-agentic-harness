using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.Common.Config;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Infrastructure.AI.KnowledgeGraph.InMemory;
using Infrastructure.AI.KnowledgeGraph.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Infrastructure.AI.KnowledgeGraph.Tests.Memory;

/// <summary>
/// Tests for <see cref="KnowledgeMemoryService"/> — remember/recall/forget/improve
/// operations with two-source retrieval, scope-namespaced isolation, and feedback integration.
/// </summary>
public sealed class KnowledgeMemoryServiceTests
{
    private readonly InMemorySessionCache _cache;
    private readonly InMemoryGraphStore _graphStore;
    private readonly FakeKnowledgeScope _scope;
    private readonly Mock<IFeedbackDetector> _feedbackDetector;
    private readonly Mock<IFeedbackStore> _feedbackStore;
    private readonly Mock<IOptionsMonitor<AppConfig>> _configMonitor;
    private readonly KnowledgeMemoryService _service;

    public KnowledgeMemoryServiceTests()
    {
        _cache = new InMemorySessionCache();
        _graphStore = new InMemoryGraphStore(NullLogger<InMemoryGraphStore>.Instance);
        _scope = new FakeKnowledgeScope();
        _feedbackDetector = new Mock<IFeedbackDetector>();
        _feedbackStore = new Mock<IFeedbackStore>();
        _configMonitor = new Mock<IOptionsMonitor<AppConfig>>();
        _configMonitor.Setup(m => m.CurrentValue).Returns(new AppConfig
        {
            AI = new AIConfig
            {
                Rag = new RagConfig
                {
                    GraphRag = new GraphRagConfig { FeedbackAlpha = 0.3 }
                }
            }
        });

        _service = CreateService(_scope, _cache);
    }

    // Each call models a distinct request: its own scoped session cache, sharing only the
    // singleton graph store (the actual cross-session/cross-user surface). Defaults to a fresh
    // cache so isolation tests don't accidentally share within-request state.
    private KnowledgeMemoryService CreateService(IKnowledgeScope scope, ISessionKnowledgeCache? cache = null) => new(
        cache ?? new InMemorySessionCache(),
        _graphStore,
        scope,
        _feedbackDetector.Object,
        _feedbackStore.Object,
        _configMonitor.Object,
        NullLogger<KnowledgeMemoryService>.Instance);

    // memory:{tenant}:{user}:{key} — with an unset scope this is the shared default namespace.
    private const string DefaultNs = "memory:default:anon";

    [Fact]
    public async Task Remember_AddsToSessionCache()
    {
        await _service.RememberAsync("Azure", "Cloud platform by Microsoft");

        _cache.Count.Should().Be(1);
        var results = _cache.Search("Azure");
        results.Should().HaveCount(1);
        results[0].Properties["content"].Should().Be("Cloud platform by Microsoft");
    }

    [Fact]
    public async Task Remember_WritesThroughToDurableGraphStore()
    {
        await _service.RememberAsync("Azure", "Cloud platform by Microsoft");

        // Durability: the fact must survive the request scope, so it is persisted to the graph
        // store (not only the per-request session cache, which is discarded at scope end).
        var persisted = await _graphStore.GetNodeAsync($"{DefaultNs}:azure");
        persisted.Should().NotBeNull();
        persisted!.Properties["content"].Should().Be("Cloud platform by Microsoft");
    }

    [Fact]
    public async Task Remember_StampsOwnerIdFromScope()
    {
        _scope.UserId = "user-a";
        var service = CreateService(_scope);

        await service.RememberAsync("Azure", "Cloud platform");

        var persisted = await _graphStore.GetNodeAsync("memory:default:user-a:azure");
        persisted!.OwnerId.Should().Be("user-a");
    }

    [Fact]
    public async Task Remember_UsesCustomEntityType()
    {
        await _service.RememberAsync("Python", "Programming language", "Technology");

        var results = _cache.Search("Python");
        results[0].Type.Should().Be("Technology");
    }

    [Fact]
    public async Task Recall_FromCacheFirst()
    {
        await _service.RememberAsync("Azure", "Cloud platform");

        var results = await _service.RecallAsync("Azure");

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Azure");
    }

    [Fact]
    public async Task Recall_FallsBackToGraph_WhenCacheEmpty()
    {
        // Seed graph with a scope-namespaced memory ID (matching RememberAsync's ID pattern).
        var node = new GraphNode
        {
            Id = $"{DefaultNs}:kubernetes", Name = "Kubernetes", Type = "Technology",
            ChunkIds = ["c1"]
        };
        await _graphStore.AddNodesAsync([node]);

        var results = await _service.RecallAsync("Kubernetes");

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Kubernetes");
    }

    [Fact]
    public async Task Recall_DeduplicatesBetweenCacheAndGraph()
    {
        await _service.RememberAsync("Azure", "From cache");

        var results = await _service.RecallAsync("Azure", maxResults: 10);

        // RememberAsync wrote the same scope-namespaced node to both cache and graph; recall
        // must not return it twice.
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task Forget_RemovesFromCacheAndGraph()
    {
        await _service.RememberAsync("Temp", "Temporary fact");

        await _service.ForgetAsync("Temp");

        _cache.Search("Temp").Should().BeEmpty();
        (await _graphStore.GetNodeAsync($"{DefaultNs}:temp")).Should().BeNull();
    }

    // --- Cross-user / cross-tenant isolation (the security guarantee that gates KnowledgeBridge) ---

    [Fact]
    public async Task Recall_DoesNotReturnAnotherUsersFact_SameTenant()
    {
        var userA = CreateService(new FakeKnowledgeScope { UserId = "user-a", TenantId = "tenant-1" });
        var userB = CreateService(new FakeKnowledgeScope { UserId = "user-b", TenantId = "tenant-1" });

        await userA.RememberAsync("favorite_color", "blue");

        // User B asks for the same key against the SAME shared graph store.
        var recalled = await userB.RecallAsync("favorite_color");

        recalled.Should().BeEmpty("user B must never see user A's remembered facts");
    }

    [Fact]
    public async Task Recall_DoesNotReturnAnotherTenantsFact_SameUserKey()
    {
        var tenant1 = CreateService(new FakeKnowledgeScope { UserId = "shared-id", TenantId = "tenant-1" });
        var tenant2 = CreateService(new FakeKnowledgeScope { UserId = "shared-id", TenantId = "tenant-2" });

        await tenant1.RememberAsync("secret", "tenant-1 data");

        var recalled = await tenant2.RecallAsync("secret");

        recalled.Should().BeEmpty("tenant isolation must hold even when the user id collides across tenants");
    }

    [Fact]
    public async Task Recall_ReturnsOwnFact_WhenScopeMatches()
    {
        var userA = CreateService(new FakeKnowledgeScope { UserId = "user-a", TenantId = "tenant-1" });

        await userA.RememberAsync("favorite_color", "blue");
        var recalled = await userA.RecallAsync("favorite_color");

        recalled.Should().ContainSingle();
        recalled[0].Properties["content"].Should().Be("blue");
    }

    [Fact]
    public async Task Improve_WithFeedback_AppliesWeights()
    {
        _feedbackDetector
            .Setup(d => d.DetectFeedbackAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new FeedbackDetectionResult
            {
                FeedbackDetected = true,
                FeedbackScore = 5,
                FeedbackText = "Positive",
                ContainsFollowupQuestion = false
            });

        await _service.ImproveAsync("Great answer!", "Here is info...", ["n1", "n2"]);

        _feedbackStore.Verify(
            f => f.ApplyNodeFeedbackAsync("n1", 5, 0.3, default), Times.Once);
        _feedbackStore.Verify(
            f => f.ApplyNodeFeedbackAsync("n2", 5, 0.3, default), Times.Once);
    }

    [Fact]
    public async Task Improve_NoFeedbackDetected_SkipsWeightUpdate()
    {
        _feedbackDetector
            .Setup(d => d.DetectFeedbackAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new FeedbackDetectionResult
            {
                FeedbackDetected = false,
                ContainsFollowupQuestion = false
            });

        await _service.ImproveAsync("ok", "response", ["n1"]);

        _feedbackStore.Verify(
            f => f.ApplyNodeFeedbackAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), default),
            Times.Never);
    }

    [Fact]
    public async Task Improve_NullDetectorAndStore_SkipsGracefully()
    {
        var service = new KnowledgeMemoryService(
            _cache, _graphStore, _scope, null, null,
            _configMonitor.Object,
            NullLogger<KnowledgeMemoryService>.Instance);

        await service.ImproveAsync("test", "response", ["n1"]);
        // Should not throw
    }

    /// <summary>Mutable <see cref="IKnowledgeScope"/> stub for exercising scope-dependent behavior.</summary>
    private sealed class FakeKnowledgeScope : IKnowledgeScope
    {
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? DatasetId { get; set; }
        public string? DatasetName { get; set; }
        public string? DatasetOwnerId { get; set; }
        public string? AgentId { get; set; }
        public string? ConversationId { get; set; }
    }
}
