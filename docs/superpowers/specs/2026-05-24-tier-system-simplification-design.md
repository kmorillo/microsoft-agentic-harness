# Tier System Simplification

## Problem

The 3-tier progressive disclosure system (Index Card / Folder / Filing Cabinet) was designed for registry-scale skill discovery but was prematurely applied to agent execution contexts. A comprehensive audit reveals that **all tier-related infrastructure is dead code** — defined, tested, but with zero runtime consumers:

- **Token budgeting** (`Level1/2/3TokenEstimate`, `IsLevel2Oversized`, `TotalLoadedTokenEstimate`) — computed but never read for enforcement.
- **Loading state** (`IsFullyLoaded`) — always hardcoded to `true` by the parser, never checked at runtime.
- **Context loading** (`ContextLoading`, `ContextTierConfig`) — a full 3-tier file loading system with Required, Files, FromDependencies, LookupPaths, FallbackPrompt, MaxTokens. Zero consumers.
- **Context contract** (`ContextContract`) — input/output contract specification. Zero consumers.
- **Resource loading options** (6 properties on `SkillAgentOptions`) — selective resource loading flags. Never read.

Skills are always loaded eagerly with full content. No budget enforcement exists. The infrastructure adds ~200 lines of dead code and 3 test files testing dead code.

## Decision

Surgical removal of all dead tier infrastructure. Keep the SKILL.md resource schema (Templates, References, Scripts, Assets) since it's part of the structural data model populated by the parser. If progressive disclosure is needed later, rebuild from actual requirements rather than speculative architecture.

## Scope

### Delete (entire files)

| File | Lines | Purpose |
|------|-------|---------|
| `src/Content/Domain/Domain.AI/Skills/ContextLoading.cs` | ~82 | 3-tier context file loading config |
| `src/Content/Domain/Domain.AI/Skills/ContextContract.cs` | ~50 | Input/output contract spec |
| `src/Content/Tests/Domain.AI.Tests/Skills/SkillDefinitionTokenEstimateTests.cs` | ~60 | Tests for dead token estimates |
| `src/Content/Tests/Domain.AI.Tests/Skills/ContextLoadingTests.cs` | ~40 | Tests for dead ContextLoading |
| `src/Content/Tests/Domain.AI.Tests/Skills/ContextContractTests.cs` | ~50 | Tests for dead ContextContract |

### Modify

#### `src/Content/Domain/Domain.AI/Skills/SkillDefinition.cs`

**Remove:**
- `ContextContract` property and XML doc
- `ContextLoading` property and XML doc
- `IsFullyLoaded` property
- `HasContextContract` computed property
- `HasContextLoading` computed property
- Entire `#region Progressive Disclosure Metrics` (Level1/2/3TokenEstimate, TotalLoadedTokenEstimate, IsLevel2Oversized)

**Keep:**
- Resource collections (Templates, References, Scripts, Assets) and their `Has*` checks
- `TotalResourceCount`
- All other properties and computed checks

#### `src/Content/Domain/Domain.AI/Skills/SkillAgentOptions.cs`

**Remove entire `#region Resource Loading`:**
- `LoadAllTemplates`
- `LoadAllReferences`
- `TemplatesToLoad`
- `ReferencesToLoad`
- `IncludeResourcesInInstructions`
- `IncludeResourceManifest`

**Keep:** All other properties (SkillPaths, AgentNameOverride, DeploymentName, AgentId, etc.) — all used at runtime.

#### `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs`

**Remove:** `IsFullyLoaded = true,` assignment at both call sites (ParseFromFile and Parse methods).

### Keep (explicitly not touching)

| Item | Reason |
|------|--------|
| `SkillResource`, `SkillResourceType` | Structural data model for SKILL.md resources |
| Templates, References, Scripts, Assets collections | Populated by parser, part of the schema |
| `HasTemplates`, `HasReferences`, `HasScripts`, `HasAssets` | Lightweight checks for populated collections |
| `TotalResourceCount` | Simple sum, no tier semantics |
| `SkillsTier` telemetry constant | Actually used in telemetry recording |
| `ISkillContentProvider` | Provides on-demand file content, not tied to tier loading |

## Risk

Near-zero. All deleted code is verified unused at runtime through exhaustive grep analysis. No production code reads any of the deleted properties. Tests for deleted code are deleted alongside it. The resource collections remain available for future use.

## Testing

- Full solution build must succeed with 0 errors after deletion.
- Full test suite must pass (minus the deleted test files).
- No runtime behavior change since deleted code was never invoked.
