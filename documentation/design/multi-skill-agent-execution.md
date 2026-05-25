# Multi-Skill Agent Execution

## Problem

The harness maps one skill to one agent execution context. `AgentDefinition` has a single `Skill` property. `AgentFactory.CreateAgentFromSkillAsync` takes one skill ID. `AgentExecutionContextFactory.MapToAgentContextAsync` takes one `SkillDefinition`. An agent that needs both "research-topic" and "make-ppt" capabilities requires two separate agents coordinated by a supervisor — two LLM calls, two contexts, no shared state.

Claude Code loads all relevant skills into the system prompt simultaneously and the LLM self-orchestrates. The harness should work the same way.

## Goals

1. An agent declares multiple skills in its manifest — all load into one execution context
2. LLM sees all skills' instructions and tools at once, self-orchestrates between them
3. Agent-level `AllowedTools` acts as a whitelist filter over skill tools
4. Required skills that can't resolve fail agent creation; optional skills warn and continue
5. Fully backward compatible — single-skill agents work identically

## Non-Goals

- Harness-controlled skill switching per turn (LLM self-orchestrates)
- Tier-based progressive disclosure at execution time (all declared skills load fully)
- Skill prerequisites enforcement (Gap #2, separate work)
- Plugin skill consumption (Gap #4, separate work)

---

## Approach: Widen the Pipeline

Change each touchpoint in the execution pipeline from single-skill to multi-skill. No new classes — existing types widen to accept lists.

### Pipeline Flow

```
AgentDefinition.Skills: ["research-topic", "make-ppt"]
    |
    v
ExecuteAgentTurnCommandHandler
    resolves full Skills list (fallback: treat agent name as single skill)
    |
    v
AgentConversationCache.GetOrCreateAsync(conversationId, skillIds, options)
    cache key: conversationId (unchanged)
    |
    v
AgentFactory.CreateAgentFromSkillsAsync(skillIds, options)
    resolves each skillId via ISkillMetadataRegistry.TryGet()
    required skill missing -> fail
    optional skill missing -> warn, continue
    |
    v
AgentExecutionContextFactory.MapToAgentContextAsync(List<SkillDefinition>, options)
    |--- Instructions: concat all skills' instructions with section headers
    |--- Tools: merge from all skills, deduplicate, apply AllowedTools whitelist
    |--- AIContextProviders: union from all skills
    |--- Middleware: union from all skills
    |
    v
AgentExecutionContext (unchanged type)
    Instruction: string (merged)
    Tools: IList<AITool> (merged)
    |
    v
AIAgent.RunAsync(messages) -- unchanged, LLM self-orchestrates
```

---

## Changes by File

### 1. AgentDefinition.cs

```csharp
// Before
public string? Skill { get; init; }

// After
public IReadOnlyList<string> Skills { get; init; } = [];
```

### 2. AgentManifest Parser

Support two formats for declaring skills:

**YAML frontmatter shorthand:**
```yaml
---
name: content-agent
skills: [research-topic, make-ppt]
---
```

**Markdown table (existing format, gains Required column):**
```markdown
## Skills
| Skill ID       | Required | Context                    |
| research-topic | yes      | RAG-powered topic research |
| make-ppt       | no       | PowerPoint generation      |
```

Both produce `AgentDefinition.Skills = ["research-topic", "make-ppt"]`.

The `SkillReference` type gains or uses its existing `IsRequired` property. When using the YAML shorthand, all skills default to required.

### 3. ExecuteAgentTurnCommandHandler.cs

```csharp
// Before
var skillId = _agentRegistry.TryGet(request.AgentName)?.Skill ?? request.AgentName;

// After
var agentDef = _agentRegistry.TryGet(request.AgentName);
var skillIds = agentDef?.Skills is { Count: > 0 }
    ? agentDef.Skills
    : [request.AgentName];
```

Passes `skillIds` list to cache. `SkillAgentOptions` construction unchanged.

### 4. AgentConversationCache.cs

Signature widens:
```csharp
// Before
public async Task<AIAgent> GetOrCreateAsync(
    string conversationId, string skillId, SkillAgentOptions options, ...)

// After
public async Task<AIAgent> GetOrCreateAsync(
    string conversationId, IReadOnlyList<string> skillIds, SkillAgentOptions options, ...)
```

Cache key remains `conversationId`. Passes `skillIds` to factory.

### 5. AgentFactory.cs

New overload:
```csharp
public async Task<AIAgent> CreateAgentFromSkillsAsync(
    IReadOnlyList<string> skillIds,
    SkillAgentOptions options,
    CancellationToken cancellationToken = default)
```

Resolution logic:
- For each skill ID, call `_skillRegistry.TryGet(id)`
- Required skill not found: throw `InvalidOperationException` with clear message
- Optional skill not found: log warning, skip
- Zero skills resolved: throw
- Pass resolved list to `MapToAgentContextAsync`

Existing single-skill overload becomes wrapper:
```csharp
public Task<AIAgent> CreateAgentFromSkillAsync(string skillId, ...)
    => CreateAgentFromSkillsAsync([skillId], options, cancellationToken);
```

### 6. AgentExecutionContextFactory.cs

New overload:
```csharp
public async Task<AgentExecutionContext> MapToAgentContextAsync(
    IReadOnlyList<SkillDefinition> skills,
    SkillAgentOptions options)
```

Existing single-skill overload becomes wrapper:
```csharp
public Task<AgentExecutionContext> MapToAgentContextAsync(SkillDefinition skill, ...)
    => MapToAgentContextAsync([skill], options);
```

#### Instruction Merging

Concatenate all skills' instructions in list order with section headers:

```
[Agent-level instructions from options.AdditionalContext]

## Skill: research-topic
[research-topic's full instructions]

## Skill: make-ppt
[make-ppt's full instructions]
```

No tier filtering. All declared skills load fully.

#### Tool Merging + AllowedTools Whitelist

```
1. For each skill:
   a. Run existing 4-pass tool resolution:
      - Pre-created tools (skill.Tools)
      - ToolDeclarations (MCP-first, keyed-DI-fallback)
      - AllowedTools simple name list (keyed DI)
      - AdditionalTools from options
   b. Collect resolved tools

2. Merge all resolved tools into one list

3. Deduplicate by tool name (same tool from two skills = one entry)

4. If agent manifest has AllowedTools (whitelist):
   a. Filter: keep only tools whose name appears in AllowedTools
   b. For each required tool filtered out: fail with clear error
      "Agent 'content-agent' AllowedTools excludes required tool
       'file_delete' declared by skill 'cleanup-files'"
   c. For each optional tool filtered out: log info, continue

5. If AllowedTools is null/empty: no filtering (backward compatibility)

6. Return final tool list
```

This also fixes Gap #8 (required-tool enforcement).

#### AIContextProviders + Middleware

- AIContextProviders: union from all skills, deduplicate by type
- Middleware: union from all skills, deduplicate by type

---

## What Doesn't Change

| Component | Why unchanged |
|---|---|
| `AgentExecutionContext` | `Instruction` stays `string`, `Tools` stays `IList<AITool>`. It's the merged output. |
| `AIAgent.RunAsync()` | Sees one system prompt, one tool list. Doesn't know about multi-skill. |
| `SkillDefinition` | Each skill still declares its own tools/instructions independently. |
| `ISkillMetadataRegistry` | Callers loop and call `TryGet` per ID. No batch method needed. |
| `CreateAgentAsync` | Takes `AgentExecutionContext`, wraps in middleware. Doesn't care how context was assembled. |
| Microsoft Agent Framework | `ChatClientAgent`, `ChatOptions`, `AITool` — all unchanged. |

---

## Backward Compatibility

- Single-skill agents: `Skills = ["my-skill"]` — list of one, identical behavior
- No-skills agents: fallback to `request.AgentName` as skill ID (current behavior)
- Existing AGENT.md files with Skills table: parsed into `Skills` list, works as before
- `AllowedTools` not set: no whitelist filtering, all skill tools available (current behavior)
- Single-skill `CreateAgentFromSkillAsync` overload: preserved, delegates to multi-skill version

---

## Example: Multi-Skill Agent

### AGENT.md
```markdown
---
name: content-agent
domain: content-creation
allowed_tools: [document_search, document_ingest, ppt_create, ppt_add_slide, ppt_export]
---

## Skills
| Skill ID       | Required | Context                    |
| research-topic | yes      | RAG-powered topic research |
| make-ppt       | no       | PowerPoint generation      |

You are a content creation agent. Research topics thoroughly,
then produce polished presentations.
```

### Resulting AgentExecutionContext

**Instruction:**
```
You are a content creation agent. Research topics thoroughly,
then produce polished presentations.

## Skill: research-topic
Use RAG pipeline to search, synthesize, and cite sources...
[full research-topic instructions]

## Skill: make-ppt
Create presentations using python-pptx...
[full make-ppt instructions]
```

**Tools:**
```
document_search    (from research-topic, keyed DI)
document_ingest    (from research-topic, keyed DI)
ppt_create         (from make-ppt, plugin MCP)
ppt_add_slide      (from make-ppt, plugin MCP)
ppt_export         (from make-ppt, plugin MCP)
```

All five pass the AllowedTools whitelist. If make-ppt also exposed `ppt_delete` but it's not in AllowedTools, it gets filtered out (optional tool, logged and skipped).

---

## Test Plan

1. Single-skill agent still works identically (backward compat)
2. Multi-skill agent gets merged instructions with section headers
3. Multi-skill agent gets deduplicated merged tools
4. AllowedTools whitelist filters tools correctly
5. Required skill missing fails agent creation
6. Optional skill missing warns and continues
7. Required tool filtered by AllowedTools fails with clear error
8. Optional tool filtered by AllowedTools logs info and continues
9. No AllowedTools set means no filtering (backward compat)
10. Agent name fallback (no registry entry) still works as single skill

---

## Decisions

1. **All-at-once orchestration** — LLM sees all skills simultaneously, no harness-controlled skill switching
2. **No tier filtering at execution** — all declared skills load fully (Gap #7 simplification)
3. **AllowedTools = whitelist** — agent-level constraint filters skill tools
4. **Required/optional skills** — declared per skill in manifest, enforced at creation time
5. **Both manifest formats** — YAML shorthand `skills: [a, b]` and markdown Skills table
6. **Tool deduplication by name** — same tool from multiple skills = one entry, no collision logic needed
