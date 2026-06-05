using Application.AI.Common.Interfaces.Agent;
using Domain.Common.Config;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Infrastructure.AI.KnowledgeGraph.Scoping;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Infrastructure.AI.KnowledgeGraph.Tests.Scoping;

/// <summary>
/// Tests for <see cref="KnowledgeScopeAccessor"/> — verifies the ambient (AsyncLocal) user/tenant
/// identity flows across DI-scope boundaries and background continuations, which is what keeps
/// memory isolation intact on the orchestrator / sub-plan / post-turn-write paths.
/// </summary>
public sealed class KnowledgeScopeAccessorTests
{
    private static KnowledgeScopeAccessor Create()
    {
        var agentContext = new Mock<IAgentExecutionContext>();
        var configMonitor = new Mock<IOptionsMonitor<AppConfig>>();
        configMonitor.Setup(m => m.CurrentValue).Returns(new AppConfig
        {
            AI = new AIConfig
            {
                Rag = new RagConfig
                {
                    GraphRag = new GraphRagConfig { DefaultTenantId = "cfg-tenant" }
                }
            }
        });
        return new KnowledgeScopeAccessor(agentContext.Object, configMonitor.Object);
    }

    [Fact]
    public void SetScope_IsObservedByAnotherInstance_SimulatingChildScope()
    {
        // The orchestrator / DAG planner run sub-agents in a fresh DI scope, which resolves a
        // DIFFERENT accessor instance. Identity must still flow to it via the ambient AsyncLocal.
        var entryScope = Create();
        var childScope = Create();

        entryScope.SetScope(userId: "user-a", tenantId: "tenant-1");

        childScope.UserId.Should().Be("user-a");
        childScope.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task SetScope_FlowsIntoBackgroundContinuation()
    {
        // The conversation-to-knowledge write runs on a post-turn Task.Run after the request scope
        // is gone; the captured execution context must still carry the caller's identity.
        var accessor = Create();
        accessor.SetScope(userId: "user-a", tenantId: "tenant-1");

        string? observedUser = null;
        await Task.Run(() => observedUser = accessor.UserId);

        observedUser.Should().Be("user-a");
    }

    [Fact]
    public void Scope_FallsBackToConfigDefaultTenant_WhenUnset()
    {
        var accessor = Create();

        accessor.UserId.Should().BeNull();
        accessor.TenantId.Should().Be("cfg-tenant");
    }
}
