# Task #6 — PR 3: Tenant-Level Isolation (all backends)

**Branch:** `feat/task6-tenant-level-isolation` (stacked on PR 2 / `feat/task6-multitenant-memory-isolation`)
**Date:** 2026-06-05
**Scope chosen by user:** Option B — fix everything now. Tenant isolation must actually work on all three backends, not just in-memory.

## Why this is bigger than "add a field"

The default backend (`managed_code` → in-memory) is lossless, so PR 2's owner isolation works there. But the opt-in **Neo4j** and **PostgreSQL** backends are incomplete (pre-existing TODOs):
- `AddNodesAsync`/`AddEdgesAsync` **drop** `OwnerId`, `CreatedAt`, `ExpiresAt` (Neo4j also drops `Provenance`).
- `GetAllNodesAsync` and `GetNodesByOwnerAsync` are **stubs returning `[]`**.
- There is **no schema-creation code** for Postgres (tables assumed to pre-exist).
- Neither backend has **any tests**.

Consequence: on those backends every node reads back with `OwnerId == null`, so PR 2's decorator treats everything as shared → the owner/tenant filter is a silent no-op, and retention/erase-by-owner don't function. Adding `TenantId` only to the model would inherit the same silent no-op. So PR 3 must complete the backend persistence too.

Verification is feasible: **Docker is available** and Testcontainers is already used (`Presentation.AgentHub.Tests`), so Neo4j/Postgres code can be tested against real containers.

## Design

### Isolation model (the rule every read enforces)
A record is visible to a caller iff **tenant matches AND owner matches**:
- **tenantOk** = `node.TenantId is null` (global/shared-across-tenants) OR `validator.ValidateAccess(scope, node.TenantId)` (same tenant).
- **ownerOk** = `node.OwnerId is null` (shared within the tenant — corpus/learnings) OR `validator.CanAccessDataset(scope, node.OwnerId)` (caller owns it — memory).
- No ambient scope (background/system) = full access (unchanged).

This yields: a tenant's ingested corpus is shared among that tenant's users but invisible to other tenants; a user's memory is private to them within their tenant; truly global reference data (null tenant) is visible to all.

### 1. Domain model
- `GraphNode` + `public string? TenantId { get; init; }` (XML: "tenant that owns this node; null = global, visible across all tenants").
- `GraphEdge` + same.

### 2. Tenant stamping — `ComplianceAwareGraphStore`
Symmetric with temporal stamping, and the deliberate **opposite** of the owner decision: stamp `TenantId = node.TenantId ?? CurrentTenantId` on `AddNodesAsync`/`AddEdgesAsync` (CurrentTenantId = ambient `scope.TenantId`).
- Owner is *not* defaulted (null owner = shared within tenant). Tenant *is* defaulted to the writer's tenant, because that is what makes corpus tenant-gated; a write with no tenant context (null ambient = system) stays null = global.
- Add a `CurrentTenantId` helper mirroring the existing `CurrentUserId`.

### 3. Memory path — `KnowledgeMemoryService`
`RememberAsync` sets `TenantId = scope.TenantId` on the node explicitly (alongside the existing `OwnerId = scope.UserId`), so memory nodes are self-describing.

### 4. Decorator — `TenantIsolatedGraphStore`
Change `CanAccess(string? ownerId, scope)` → `CanAccess(string? tenantId, string? ownerId, scope)` implementing tenantOk && ownerOk above. Update every call site (GetNode, GetNeighbors seed + results, GetTriplets endpoints, GetAllNodes, GetNodesByOwner, GetNodeCount, AddNodes/AddEdges write filter, DeleteNode) to pass `n.TenantId, n.OwnerId`. System-scope full-access path unchanged.

### 5. Validator — `KnowledgeScopeValidator`
Reuse existing `ValidateAccess(scope, targetTenantId)` for the tenant check (already `scope.TenantId == targetTenantId`, denies on either null). Decorator handles the null-tenant=global case before calling it. No new method. Owner check (`CanAccessDataset`) unchanged.

### 6. Backends — round-trip owner/tenant/temporal + implement stub queries
- **InMemory:** round-trips `TenantId` automatically (full-record storage); `GetAllNodes`/`GetNodesByOwner` already work. No production change; add tenant assertions in tests.
- **Neo4j:**
  - `AddNodesAsync` SET: add `owner_id`, `tenant_id`, `created_at` (ISO-8601 string), `expires_at` (ISO-8601 string), `provenance` (JSON). Same for `AddEdgesAsync`.
  - `MapNode`/`MapEdge`: rehydrate those fields (parse ISO timestamps, deserialize provenance).
  - Implement `GetAllNodesAsync` (`MATCH (n:Entity) RETURN n`) and `GetNodesByOwnerAsync` (`... WHERE n.owner_id = $ownerId`).
- **PostgreSQL:**
  - Add idempotent `EnsureSchemaAsync` (`CREATE TABLE IF NOT EXISTS kg_nodes/kg_edges (... full columns incl owner_id, tenant_id, created_at, expires_at ...)`), invoked once per store (guarded flag) on first connection — makes the backend self-contained.
  - `AddNodesAsync`/`AddEdgesAsync` INSERT: add the 4 columns + params.
  - All SELECTs (GetNode, GetNeighbors, GetTriplets): add the 4 columns; update `ReadNode` + triplet reconstruction column indices.
  - Implement `GetAllNodesAsync` and `GetNodesByOwnerAsync`.

### 7. Tests
- **Domain:** TenantId on both records (with-expression, default null).
- **Decorator (in-memory — runs locally):** cross-tenant hidden; same-tenant shared corpus visible; same-tenant other-user memory hidden; null-tenant global visible to all; system full access. Extend `TenantIsolatedGraphStoreTests`.
- **Compliance:** TenantId stamped from ambient tenant; explicit preserved; null ambient → null tenant.
- **Neo4j (Testcontainers/Docker):** new `Neo4jGraphStoreTests` — owner/tenant/temporal round-trip, GetAllNodes, GetNodesByOwner. Docker-gated (skip if unavailable).
- **PostgreSQL (Testcontainers/Docker):** new `PostgreSqlGraphStoreTests` — schema init, round-trip, GetAllNodes, GetNodesByOwner, retention purge of expired.

### 8. Retention / erasure
With the stub query methods implemented, `RetentionEnforcementService` and `EraseByOwnerAsync` now function on Neo4j/Postgres for free; covered by a Postgres Testcontainers test.

### 9. Docs
Update `CLAUDE.md` (Multi-Tenant Knowledge Isolation bullet) + the task6 plan: tenant isolation enforced across all 3 backends; corpus is now tenant-gated (drop the "globally shared until PR 3" caveat); backend persistence of owner/tenant/temporal completed; Postgres schema is now self-initializing.

## Phasing (single PR, logical commits)
1. Domain model (TenantId) + in-memory tests.
2. Decorator + validator tenant filter + Compliance tenant stamping + memory path + tests (runs locally — proves the model on the default backend).
3. Neo4j backend completion + Testcontainers tests.
4. PostgreSQL backend completion (incl. schema init) + Testcontainers tests.
5. Docs.

Each phase: `dotnet build` + `dotnet test` green before the next. `/code-review` at the end. Verification uses Docker-backed integration tests for the DB backends.

## Risk / verification notes
- Postgres `GetTripletsAsync` column-index shifts (adding 4 cols × source/target/edge) are fiddly — the Testcontainers test is the guard.
- Estimated ~20-25 files. Largest blast radius of the three PRs.
- New test-project dependencies: `Testcontainers.PostgreSql`, `Testcontainers.Neo4j` (mirror the existing Testcontainers usage).
