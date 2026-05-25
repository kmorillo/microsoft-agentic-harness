# Plugin-Boundary Governance (Gap #5)

## Problem

Plugin skills loaded in Injected mode (`SkillMode.Injected`) get **all MCP tools** via `GetAllToolsAsync()` with no filtering. There is no way for harness operators to constrain which tools a plugin can access or what autonomy level its tools operate under.

This means:
- A plugin intended only for file search could invoke dangerous tools (bash, deploy, etc.)
- No enterprise control over plugin blast radius
- Plugin authors can't be constrained without per-skill `allowed-tools` declarations (which defeats the purpose of Injected mode)

The harness already has a full governance stack — `AutonomyLevel` enum, `AutonomyTierRuleProvider`, `ThreePhasePermissionResolver`, `ToolPermissionRule` with 9 sources, `ToolPermissionBehavior` MediatR pipeline behavior. Plugin governance should integrate with this existing infrastructure, not build parallel machinery.

## Decision

Extend `PluginDeclaration` (the harness operator's config) with three governance fields. Enforce at two layers:

1. **Provisioning-time** (tool list construction) — filter the tool list before the LLM ever sees it
2. **Execution-time** (permission resolution) — enforce autonomy level via the existing 3-phase permission resolver

Plugin authors write pure markdown skills. Harness operators control blast radius via `appsettings.json` plugin configuration.

## Design

### 1. Config Extension — `PluginDeclaration`

**File:** `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginDeclaration.cs`

Add three new properties:

```csharp
/// <summary>
/// Tool name whitelist. When non-null, only these tools are provisioned for
/// this plugin's Injected skills. Evaluated before <see cref="DeniedTools"/>.
/// </summary>
public IReadOnlyList<string>? AllowedTools { get; set; }

/// <summary>
/// Tool name blacklist. Matched tools are removed after AllowedTools filtering.
/// DeniedTools wins when a tool appears in both lists.
/// </summary>
public IReadOnlyList<string>? DeniedTools { get; set; }

/// <summary>
/// Autonomy level override for all tools from this plugin. When set, emits
/// permission rules via <see cref="PluginPermissionRuleProvider"/> into the
/// existing 3-phase permission resolver.
/// </summary>
public AutonomyLevel? AutonomyLevel { get; set; }
```

**Config example (`appsettings.json`):**

```json
{
  "Plugins": [
    {
      "Name": "azure-skills",
      "Path": "./plugins/azure-skills",
      "Enabled": true,
      "AllowedTools": ["az_cli", "file_search", "read_file"],
      "DeniedTools": ["bash", "deploy_production"],
      "AutonomyLevel": "Supervised"
    }
  ]
}
```

**Precedence:** `DeniedTools` wins over `AllowedTools`. If a tool appears in both, it is denied.

### 2. Provisioning-Time Enforcement

**File:** `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs`

In the existing Injected mode block within `BuildToolsAsync`, after fetching all MCP tools, apply the plugin's tool boundary:

```csharp
// After: var allMcpTools = await _mcpToolProvider.GetAllToolsAsync();
// Apply plugin tool boundary
var declaration = FindPluginDeclaration(skill.PluginSource);
if (declaration != null)
    tools = ApplyPluginToolBoundary(tools, declaration);
```

**`ApplyPluginToolBoundary` logic:**
1. If `AllowedTools` is non-null and non-empty, keep only tools whose names appear in the list
2. If `DeniedTools` is non-null and non-empty, remove tools whose names appear in the list
3. Both filters applied sequentially (allow first, deny second)

**`FindPluginDeclaration` logic:**
- Inject `IPluginRegistry` (already available in the factory)
- Look up `LoadedPlugin` by `skill.PluginSource`
- From the loaded plugin, find the matching `PluginDeclaration` in `PluginsConfig`
- Return null if not found (no filtering applied — backwards compatible)

### 3. Execution-Time Enforcement — `PluginPermissionRuleProvider`

**File:** `src/Content/Application/Application.Core/Permissions/PluginPermissionRuleProvider.cs`

New `IPermissionRuleProvider` implementation. Needs a new `PermissionRuleSource.PluginDeclaration` enum value.

**Behavior:**
- For each loaded plugin with an `AutonomyLevel` override:
  - Emit `ToolPermissionRule` entries for each tool from that plugin
  - Map `AutonomyLevel` to `PermissionBehaviorType`:
    - `Restricted` → `PermissionBehaviorType.Ask` (force approval on every invocation)
    - `Supervised` → `PermissionBehaviorType.Ask` (same, but distinguished by source for logging)
    - `Autonomous` → `PermissionBehaviorType.Allow`
  - Also emit deny rules for any `DeniedTools` with `PermissionBehaviorType.Deny`

**Priority:** Plugin rules sit between AgentManifest and AutonomyTier in the priority chain. The 3-phase resolver already handles priority ordering — plugin rules just need the right priority value.

**Source enum addition:**

```csharp
// In PermissionRuleSource.cs
/// <summary>Rule from a plugin declaration's governance config.</summary>
PluginDeclaration
```

### 4. DI Registration

**File:** `src/Content/Application/Application.Core/DependencyInjection.cs`

Register `PluginPermissionRuleProvider` alongside the existing `AutonomyTierRuleProvider`:

```csharp
services.AddSingleton<IPermissionRuleProvider, PluginPermissionRuleProvider>();
```

### 5. `LoadedPlugin` Enhancement

The `LoadedPlugin` record needs to carry a reference back to its `PluginDeclaration` so the factory can access the governance config without re-querying config.

**File:** `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginLoader.cs`

Add `Declaration` to the record:

```csharp
public record LoadedPlugin(
    string Name,
    string Version,
    string LocalPath,
    PluginManifest Manifest,
    PluginLoadStatus Status,
    IReadOnlyList<string> SkillPaths,
    IReadOnlyList<string> McpServerNames,
    PluginDeclaration Declaration);
```

This avoids a config lookup at provisioning time — the declaration travels with the loaded plugin.

## Scope

### Create

| File | Purpose |
|------|---------|
| `Application.Core/Permissions/PluginPermissionRuleProvider.cs` | Emits permission rules from plugin autonomy config |

### Modify

| File | Change |
|------|--------|
| `Domain.Common/Config/AI/Plugins/PluginDeclaration.cs` | Add `AllowedTools`, `DeniedTools`, `AutonomyLevel` |
| `Domain.AI/Permissions/PermissionRuleSource.cs` | Add `PluginDeclaration` value |
| `Application.AI.Common/Interfaces/Plugins/IPluginLoader.cs` | Add `Declaration` to `LoadedPlugin` |
| `Application.AI.Common/Factories/AgentExecutionContextFactory.cs` | Add `ApplyPluginToolBoundary` + `FindPluginDeclaration` |
| `Infrastructure.AI/Plugins/PluginLoader.cs` | Pass `PluginDeclaration` when constructing `LoadedPlugin` |
| `Application.Core/DependencyInjection.cs` | Register `PluginPermissionRuleProvider` |

### Tests

| Test File | Coverage |
|-----------|----------|
| `Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryGovernanceTests.cs` | AllowedTools filtering, DeniedTools filtering, combined, no config = no filtering |
| `Application.Core.Tests/Permissions/PluginPermissionRuleProviderTests.cs` | Rule emission for each autonomy level, denied tools → Deny rules, no config → empty |
| `Domain.AI.Tests/Permissions/PermissionRuleSourceTests.cs` | New enum value exists |

## What This Does NOT Cover

- **Runtime tool boundary changes** — governance is startup config only, not dynamic
- **Per-skill governance within a plugin** — controls are at the plugin boundary, not per-skill
- **MCP server filtering** — `GetAllToolsAsync()` still returns all servers' tools; filtering is post-fetch by tool name, not by server origin
- **Wildcard/glob patterns in AllowedTools/DeniedTools** — exact name match only in v1

## Risk

Low. All changes are additive — existing plugins with no governance config behave identically to today (no filtering, no autonomy override). The new `PluginPermissionRuleProvider` only emits rules when `AutonomyLevel` is set on a declaration.

The `LoadedPlugin` record change is a breaking change to its constructor, but `LoadedPlugin` is only constructed in `PluginLoader` (one call site).

## Testing Strategy

- **Provisioning tests**: Mock `IPluginRegistry` + `IMcpToolProvider`, verify tool list after boundary filtering
- **Permission rule tests**: Verify correct `ToolPermissionRule` emission for each autonomy level
- **Integration**: Ensure the full pipeline (Injected skill → filtered tools → permission rules) works end-to-end via the existing `ThreePhasePermissionResolver` tests pattern
