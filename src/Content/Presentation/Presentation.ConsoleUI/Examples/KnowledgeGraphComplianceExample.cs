using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Presentation.ConsoleUI.Common.Helpers;
using Spectre.Console;

namespace Presentation.ConsoleUI.Examples;

/// <summary>
/// Demonstrates Knowledge Graph compliance features: provenance stamping, multi-tenant isolation,
/// GDPR right-to-erasure, audit logging, and retention policies.
/// </summary>
/// <remarks>
/// <para>
/// This example covers the governance layer that ensures knowledge graphs remain compliant
/// with GDPR, audit requirements, and data retention policies:
/// - Provenance tracking: Every node/edge carries metadata about its extraction source
/// - Tenant isolation: Multi-tenant deployments enforce strict access boundaries
/// - Erasure orchestration: Right-to-erasure cascades across graph, feedback, and vector stores
/// - Audit sinks: All operations are logged for compliance inspection
/// - Retention policies: Automatic expiration of stale data based on entity type
/// </para>
/// </remarks>
public class KnowledgeGraphComplianceExample
{
    private readonly IProvenanceStamper _provenanceStamper;
    private readonly IKnowledgeScopeValidator _scopeValidator;
    private readonly IErasureOrchestrator _erasureOrchestrator;
    private readonly IRetentionPolicyProvider _retentionPolicyProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KnowledgeGraphComplianceExample> _logger;

    public KnowledgeGraphComplianceExample(
        IProvenanceStamper provenanceStamper,
        IKnowledgeScopeValidator scopeValidator,
        IErasureOrchestrator erasureOrchestrator,
        IRetentionPolicyProvider retentionPolicyProvider,
        IServiceProvider serviceProvider,
        ILogger<KnowledgeGraphComplianceExample> logger)
    {
        _provenanceStamper = provenanceStamper;
        _scopeValidator = scopeValidator;
        _erasureOrchestrator = erasureOrchestrator;
        _retentionPolicyProvider = retentionPolicyProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ConsoleHelper.DisplayHeader("Knowledge Graph Compliance", Color.DodgerBlue1);
        ConsoleHelper.DisplayModeInfo(isLive: false);

        try
        {
            // Step 1: Provenance stamping
            ConsoleHelper.DisplayStep(1, 5, "Provenance Stamping");
            await ProvenanceStampingAsync();

            // Step 2: Tenant isolation
            ConsoleHelper.DisplayStep(2, 5, "Tenant Isolation & Access Control");
            await TenantIsolationAsync();

            // Step 3: GDPR erasure
            ConsoleHelper.DisplayStep(3, 5, "GDPR Right-to-Erasure");
            await GdprErasureAsync(cancellationToken);

            // Step 4: Audit logging
            ConsoleHelper.DisplayStep(4, 5, "Audit Logging");
            await AuditLoggingAsync(cancellationToken);

            // Step 5: Retention policies
            ConsoleHelper.DisplayStep(5, 5, "Retention Policies");
            await RetentionPoliciesAsync();

            ConsoleHelper.DisplaySuccess("Knowledge Graph compliance demonstration complete");
        }
        catch (Exception ex)
        {
            ConsoleHelper.DisplayError($"Error: {ex.Message}");
            _logger.LogError(ex, "Knowledge Graph compliance example failed");
        }
    }

    private Task ProvenanceStampingAsync()
    {
        AnsiConsole.MarkupLine("\n  [bold]Creating provenance stamps:[/]");

        // Create two stamps for different extraction pipelines
        var ragStamp = _provenanceStamper.CreateStamp(
            sourcePipeline: "rag-ingestion",
            sourceTask: "entity-extraction",
            sourceDocumentId: "doc-abc-123",
            extractionConfidence: 0.87,
            modifiedBy: "system-agent");

        var enrichmentStamp = _provenanceStamper.CreateStamp(
            sourcePipeline: "knowledge-enrichment",
            sourceTask: "relationship-detection",
            sourceDocumentId: "doc-xyz-789",
            extractionConfidence: 0.92,
            modifiedBy: "enrichment-service");

        // Create a sample node and edge
        var node = new GraphNode
        {
            Id = "entity-microsoft",
            Name = "Microsoft",
            Type = "Organization",
            Properties = new Dictionary<string, string> { { "sector", "Technology" } },
            ChunkIds = ["chunk-001", "chunk-042"],
        };

        var edge = new GraphEdge
        {
            Id = "rel-creates-azure",
            SourceNodeId = "entity-microsoft",
            TargetNodeId = "entity-azure",
            Predicate = "creates",
            ChunkId = "chunk-001",
        };

        // Stamp them
        var stampedNode = _provenanceStamper.StampNode(node, ragStamp);
        var stampedEdge = _provenanceStamper.StampEdge(edge, enrichmentStamp);

        // Display results
        DisplayProvenanceTable(stampedNode, stampedEdge);

        return Task.CompletedTask;
    }

    private Task TenantIsolationAsync()
    {
        AnsiConsole.MarkupLine("\n  [bold]Validating access control:[/]");

        // Create mock scopes for testing
        var ownTenantScope = new MockKnowledgeScope(
            userId: "user-alice",
            tenantId: "tenant-a",
            datasetId: "dataset-001",
            datasetName: "Alice's Workspace",
            datasetOwnerId: "user-alice",
            agentId: "agent-001",
            conversationId: "conv-001");

        var otherTenantScope = new MockKnowledgeScope(
            userId: "user-bob",
            tenantId: "tenant-b",
            datasetId: "dataset-002",
            datasetName: "Bob's Workspace",
            datasetOwnerId: "user-bob",
            agentId: "agent-002",
            conversationId: "conv-002");

        // Test access validation
        var canAccessOwn = _scopeValidator.ValidateAccess(ownTenantScope, "tenant-a", "dataset-001");
        var canAccessOther = _scopeValidator.ValidateAccess(ownTenantScope, "tenant-b", "dataset-002");
        var canAccessByOwner = _scopeValidator.CanAccessDataset(ownTenantScope, "user-alice");

        DisplayTenantIsolationTable(canAccessOwn, canAccessOther, canAccessByOwner);

        return Task.CompletedTask;
    }

    private async Task GdprErasureAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("\n  [bold]Executing right-to-erasure request:[/]");

        try
        {
            var receipt = await _erasureOrchestrator.EraseByOwnerAsync("demo-owner-123", cancellationToken);

            DisplayErasureReceiptTable(receipt);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [yellow]⚠ Erasure demo note:[/] {ex.Message}");
            AnsiConsole.MarkupLine("  [grey](In production, this would delete all data owned by the specified user)[/]");
        }
    }

    private async Task AuditLoggingAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("\n  [bold]Emitting audit event:[/]");

        try
        {
            // Try to resolve keyed IMemoryAuditSink
            var auditSink = _serviceProvider.GetKeyedService<IMemoryAuditSink>("structured_logging");

            if (auditSink is null)
            {
                auditSink = _serviceProvider.GetKeyedService<IMemoryAuditSink>("no_op");
            }

            if (auditSink is null)
            {
                AnsiConsole.MarkupLine("  [yellow]⚠[/] No audit sink configured");
                return;
            }

            var auditEvent = new MemoryAuditEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Action = MemoryAuditAction.Remember,
                ActorId = "user-compliance-demo",
                Timestamp = DateTimeOffset.UtcNow,
                ScopeId = "scope-demo-001",
                AffectedNodeIds = ["node-001", "node-002"],
                AffectedEdgeIds = null,
                Query = null,
                ResultCount = null,
            };

            await auditSink.EmitAsync(auditEvent, cancellationToken);

            DisplayAuditEventTable(auditEvent);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [yellow]⚠[/] Audit sink error: {ex.Message}");
            AnsiConsole.MarkupLine("  [grey](Check that IMemoryAuditSink is registered in DI)[/]");
        }
    }

    private Task RetentionPoliciesAsync()
    {
        AnsiConsole.MarkupLine("\n  [bold]Configured retention policies:[/]");

        var policies = _retentionPolicyProvider.GetAllPolicies();

        if (policies.Count == 0)
        {
            AnsiConsole.MarkupLine("  [yellow]⚠[/] No retention policies configured");
            return Task.CompletedTask;
        }

        DisplayRetentionPoliciesTable(policies);

        return Task.CompletedTask;
    }

    private static void DisplayProvenanceTable(GraphNode node, GraphEdge edge)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Entity[/]");
        table.AddColumn("[bold]Pipeline[/]");
        table.AddColumn("[bold]Task[/]");
        table.AddColumn("[bold]Source Doc[/]");
        table.AddColumn("[bold]Confidence[/]");
        table.AddColumn("[bold]Modified By[/]");

        if (node.Provenance is not null)
        {
            table.AddRow(
                Markup.Escape($"{node.Type}: {node.Name}"),
                Markup.Escape(node.Provenance.SourcePipeline),
                Markup.Escape(node.Provenance.SourceTask),
                Markup.Escape(node.Provenance.SourceDocumentId ?? "(synthesized)"),
                node.Provenance.ExtractionConfidence?.ToString("P1") ?? "—",
                Markup.Escape(node.Provenance.LastModifiedBy ?? "—"));
        }

        if (edge.Provenance is not null)
        {
            table.AddRow(
                Markup.Escape($"Relation: {edge.Predicate}"),
                Markup.Escape(edge.Provenance.SourcePipeline),
                Markup.Escape(edge.Provenance.SourceTask),
                Markup.Escape(edge.Provenance.SourceDocumentId ?? "(synthesized)"),
                edge.Provenance.ExtractionConfidence?.ToString("P1") ?? "—",
                Markup.Escape(edge.Provenance.LastModifiedBy ?? "—"));
        }

        AnsiConsole.Write(table);
    }

    private static void DisplayTenantIsolationTable(
        bool canAccessOwn,
        bool canAccessOther,
        bool canAccessByOwner)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Test[/]");
        table.AddColumn("[bold]Allowed[/]");
        table.AddColumn("[bold]Result[/]");

        table.AddRow(
            "Access own tenant dataset",
            canAccessOwn ? "[green]✓[/]" : "[red]✗[/]",
            canAccessOwn ? "[green]PASS[/]" : "[red]FAIL[/]");

        table.AddRow(
            "Access other tenant dataset",
            canAccessOther ? "[green]✓[/]" : "[red]✗[/]",
            !canAccessOther ? "[green]PASS[/] (correctly denied)" : "[red]FAIL[/] (incorrectly allowed)");

        table.AddRow(
            "Access dataset by owner ID",
            canAccessByOwner ? "[green]✓[/]" : "[red]✗[/]",
            canAccessByOwner ? "[green]PASS[/]" : "[red]FAIL[/]");

        AnsiConsole.Write(table);
    }

    private static void DisplayErasureReceiptTable(ErasureReceipt receipt)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Field[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("[bold]Request ID[/]", Markup.Escape(receipt.RequestId));
        table.AddRow("[bold]Scope ID[/]", Markup.Escape(receipt.ScopeId));
        table.AddRow("[bold]Requested At[/]", receipt.RequestedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        table.AddRow("[bold]Completed At[/]", receipt.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        table.AddRow("[bold]Nodes Deleted[/]", receipt.NodesDeleted.ToString());
        table.AddRow("[bold]Edges Deleted[/]", receipt.EdgesDeleted.ToString());
        table.AddRow("[bold]Feedback Weights Deleted[/]", receipt.FeedbackWeightsDeleted.ToString());
        table.AddRow("[bold]Vector Embeddings Deleted[/]", receipt.VectorEmbeddingsDeleted.ToString());

        AnsiConsole.Write(table);
    }

    private static void DisplayAuditEventTable(MemoryAuditEvent auditEvent)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Field[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("[bold]Event ID[/]", Markup.Escape(auditEvent.EventId[..16]) + "...");
        table.AddRow("[bold]Action[/]", auditEvent.Action.ToString());
        table.AddRow("[bold]Actor ID[/]", Markup.Escape(auditEvent.ActorId));
        table.AddRow("[bold]Scope ID[/]", Markup.Escape(auditEvent.ScopeId));
        table.AddRow("[bold]Timestamp[/]", auditEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        table.AddRow("[bold]Affected Nodes[/]", auditEvent.AffectedNodeIds?.Count.ToString() ?? "—");
        table.AddRow("[bold]Affected Edges[/]", auditEvent.AffectedEdgeIds?.Count.ToString() ?? "—");

        AnsiConsole.Write(table);
    }

    private static void DisplayRetentionPoliciesTable(IReadOnlyList<RetentionPolicy> policies)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Entity Type[/]");
        table.AddColumn("[bold]Retention Period[/]");
        table.AddColumn("[bold]Allow Indefinite[/]");

        foreach (var policy in policies)
        {
            table.AddRow(
                Markup.Escape(policy.EntityType),
                policy.RetentionPeriod.TotalDays > 0
                    ? $"{policy.RetentionPeriod.TotalDays:F0} days"
                    : policy.RetentionPeriod == TimeSpan.Zero
                        ? "Never"
                        : policy.RetentionPeriod.ToString(),
                policy.AllowIndefinite ? "[green]✓[/]" : "[red]✗[/]");
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Mock implementation of IKnowledgeScope for testing purposes.
    /// </summary>
    private class MockKnowledgeScope : IKnowledgeScope
    {
        public MockKnowledgeScope(
            string? userId,
            string? tenantId,
            string? datasetId,
            string? datasetName,
            string? datasetOwnerId,
            string? agentId,
            string? conversationId)
        {
            UserId = userId;
            TenantId = tenantId;
            DatasetId = datasetId;
            DatasetName = datasetName;
            DatasetOwnerId = datasetOwnerId;
            AgentId = agentId;
            ConversationId = conversationId;
        }

        public string? UserId { get; }
        public string? TenantId { get; }
        public string? DatasetId { get; }
        public string? DatasetName { get; }
        public string? DatasetOwnerId { get; }
        public string? AgentId { get; }
        public string? ConversationId { get; }
    }
}
