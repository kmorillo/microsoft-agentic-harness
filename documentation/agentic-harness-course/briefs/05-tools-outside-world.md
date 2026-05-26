# Module 5: Tools & The Outside World

## Teaching Arc
- **Metaphor:** A Swiss Army knife with a safety lock — each blade (tool) is sharp and useful, but there's a mechanism that prevents you from opening the dangerous ones without explicit permission. The MCP protocol is like a universal adapter that lets you snap in new blades from other manufacturers.
- **Opening hook:** "Skills tell the agent what it knows. Tools are what it can actually *do*. But giving an AI agent unrestricted access to your file system, APIs, and databases is a terrible idea — so the harness puts every tool behind a permission system and a security sandbox."
- **Key insight:** Tools are registered with string keys in the dependency injection container and resolved lazily — the agent only gets the tools its current skill declares. MCP extends this by letting external servers contribute tools at runtime. The plugin system goes further — local plugins bundle skills and MCP servers together, with governance filters (AllowedTools/DeniedTools) controlling which plugin tools reach the agent. Managed skills resolve tools individually; Injected skills get all tools from their plugin's MCP servers. A2A goes even further — instead of borrowing tools, agents can delegate entire tasks to other agents.
- **"Why should I care?":** When you're building with AI agents, tool selection is everything. Too many tools and the agent gets confused. Too few and it can't do the job. Understanding keyed DI and MCP means you can precisely control what your agent can and can't do — and extend its capabilities by plugging into external services.

## Screens (5)

### Screen 1: How Tools Get to the Agent (Flow Animation)
Flow animation: Skill declares tools → Factory checks skill mode → If **Managed**: resolve each tool via MCP first, fall back to keyed DI → If **Injected**: get ALL MCP tools from plugin's servers → Apply plugin governance filters (AllowedTools/DeniedTools) → Convert to AITool → Agent receives filtered tool list.

### Screen 2: The Tool Resolution Chain (Code Translation)
Code↔English of the tool provisioning logic showing MCP-first, keyed DI fallback.

### Screen 3: The Sandbox — FileSystemService (Code Translation)
Show how FileSystemService restricts file operations to allowed base paths. Code↔English translation.

### Screen 4: MCP & The Plugin System (Group Chat + Visual)
Group chat showing MCP client discovering and invoking a remote tool:
- MCPClient → ExternalServer: "What tools do you have?"
- ExternalServer → MCPClient: "I have web_search, code_analysis, image_gen"
- MCPClient → AgentFactory: "Here are 3 external tools converted to AITool"
- Agent → MCPClient: "Call web_search with query 'latest .NET 10 features'"
- MCPClient → ExternalServer: "Invoke web_search(...)"
- ExternalServer → MCPClient: "Here are the results..."
- MCPClient → Agent: "Tool returned: [results]"

**Local Plugin System** (visual below group chat):
Plugins are local directories declared in config. Each contains a `plugin.json` that wires skills and MCP servers into the harness. The harness reads the declaration, loads the plugin's skills as **Injected** skills, connects to the plugin's MCP servers, and applies governance filters before any tool reaches the agent.

Plugin-boundary governance acts as a firewall: `AllowedTools` whitelists specific tools from the plugin, `DeniedTools` blacklists dangerous ones, and `AutonomyLevel` feeds into the permission system to control how much freedom plugin tools get.

### Screen 5: A2A — Agents Talking to Agents + Quiz
Brief explanation of AgentCard discovery and task delegation, then quiz.

## Code Snippets

### Snippet 1: Tool provisioning (AgentExecutionContextFactory)
```csharp
private async Task<IEnumerable<AITool>?> ProvisionToolAsync(ToolDeclaration declaration)
{
    // Try MCP first
    if (_mcpToolProvider != null)
    {
        try
        {
            var mcpTools = await _mcpToolProvider.GetToolsAsync(declaration.Name);
            if (mcpTools?.Count > 0)
            {
                _logger.LogDebug("Resolved tool {ToolName} from MCP server", declaration.Name);
                return mcpTools;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MCP resolution failed for {ToolName}, trying keyed DI", declaration.Name);
        }
    }

    // Fallback to keyed DI
    var resolved = ResolveToolByName(declaration.Name);
    if (resolved != null)
        return resolved;

    // Try fallback tool
    if (declaration.HasFallback && !declaration.FallbackIsManual)
    {
        resolved = ResolveToolByName(declaration.Fallback!);
        if (resolved != null)
        {
            _logger.LogInformation("Using fallback tool {Fallback} for {ToolName}",
                declaration.Fallback, declaration.Name);
            return resolved;
        }
    }

    if (!declaration.Optional)
        _logger.LogWarning("Required tool {ToolName} could not be resolved", declaration.Name);

    return null;
}
```

### Snippet 1b: Plugin governance filtering
```csharp
// Plugin-boundary governance: filter tools after resolution
if (loadedPlugin?.Declaration.AllowedTools is { Count: > 0 } allowed)
    tools = tools.Where(t => allowed.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();
if (loadedPlugin?.Declaration.DeniedTools is { Count: > 0 } denied)
    tools = tools.Where(t => !denied.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();
```

### Snippet 2: ToolPermissionBehavior (safety)
```csharp
public class ToolPermissionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IToolRequest
{
    // 3-phase permission resolution:
    // Phase 1: Deny gates — absolute blocks (e.g., .git/, .ssh/)
    // Phase 2: Ask rules — requires user confirmation
    // Phase 3: Allow rules — permitted operations
}
```

### Snippet 3: A2A AgentCard
```csharp
public sealed record AgentCard
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> InputModes { get; init; } = [];
    public IReadOnlyList<string> OutputModes { get; init; } = [];
}
```

## Interactive Elements

- [x] **Data flow animation** — tool resolution chain: Skill → Factory → MCP → Keyed DI → AITool conversion. 6 actors, 8 steps.
- [x] **Code↔English translation** — ProvisionToolAsync (tool resolution) and ToolPermissionBehavior concept
- [x] **Group chat animation** — MCP client/server discovery and invocation (7 messages)
- [x] **Quiz** — 6 questions: (1) Why does the harness try MCP before keyed DI? (2) Scenario: you add a new tool but the agent can't see it — what did you forget? (3) What does "sandboxed" mean for FileSystemService? (4) Match: MCP vs A2A vs keyed DI — what each does (5) What is the difference between Managed and Injected skill mode for tool resolution? (answer: Managed resolves each declared tool individually via MCP/keyed DI; Injected gets all MCP tools from the plugin's servers and applies governance filters) (6) A plugin's DeniedTools list blocks `delete_file` — can the agent still call it? (answer: No — plugin deny rules emit bypass-immune permission rules at high priority; the tool is filtered out before the agent ever sees it)
- [x] **Glossary tooltips** — dependency injection, keyed DI, lazy resolution, MCP (Model Context Protocol), A2A (Agent-to-Agent), AITool, ITool, sandbox, allowed base paths, JWT, HTTP transport, AgentCard, tool declaration, fallback, plugin, plugin declaration, plugin.json, AllowedTools, DeniedTools, AutonomyLevel, Managed mode, Injected mode, plugin governance

## Reference Files to Read
- `references/content-philosophy.md` → all sections
- `references/gotchas.md` → all sections
- `references/interactive-elements.md` → "Message Flow / Data Flow Animation", "Code ↔ English Translation Blocks", "Group Chat Animation", "Multiple-Choice Quizzes", "Glossary Tooltips"

## Connections
- **Previous module:** "Skills" — covered what agents *know*. This module covers what they can *do*.
- **Next module:** "Seeing Inside & Staying Safe" — will cover observability and the safety systems. Tool permissions (introduced here) are part of that story but get only a brief mention here.
- **Tone/style notes:** The Swiss Army knife metaphor should be introduced early. The MCP group chat is the hero element — make it feel like a real protocol negotiation, but in friendly chat form. Keep A2A brief (one screen) — it's conceptually interesting but not the main event.
