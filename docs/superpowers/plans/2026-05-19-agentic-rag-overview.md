# Agentic RAG — Master Implementation Plan

> **For agentic workers:** Each phase has its own plan document. Execute them in order A → B → C → D. Each phase is independently shippable and produces working software.

**Goal:** Upgrade the RAG system from Level 2.5 (Modular RAG with agentic foundations) to Level 4 (Fully Autonomous RAG) across 4 sequential phases.

**Current state:** CRAG evaluation loop, LLM query classification, RAG Fusion + HyDE transformers, hybrid RRF retrieval, strategy-keyed reranking, GraphRAG entity extraction, RAPTOR summarization, feedback-weighted scoring, DocumentSearchTool agent integration, OpenTelemetry tracing.

**Architecture:** Each phase extends the existing `Infrastructure.AI.RAG` layer following the established patterns: interfaces in `Application.AI.Common`, models in `Domain.AI`, implementations in `Infrastructure.AI.RAG`, keyed DI registration, xUnit + Moq + FluentAssertions tests.

---

## Phase Dependency Map

```
Phase A: Adaptive Routing (~2 weeks)
  ├── No dependencies — starts immediately
  ├── Delivers: QueryComplexityClassifier, RetrievalDecisionGate, cost-aware routing
  └── Impact: 30-50% cost reduction, latency improvement

Phase B: Multi-Hop & Reflection (~3 weeks)
  ├── Depends on: Phase A (complexity routing determines when to trigger multi-hop)
  ├── Delivers: IterativeRetriever, QueryDecomposer, AnswerFaithfulnessEvaluator
  └── Impact: Complex multi-part questions answered correctly

Phase C: Production Graph & Memory (~4 weeks)
  ├── Depends on: Phase A (routing), soft dependency on Phase B (iterative retrieval can feed graph)
  ├── Delivers: Neo4j/Kuzu backend, Leiden communities, cross-session memory
  └── Impact: GraphRAG scales beyond 10K entities, knowledge persists across sessions

Phase D: Full Autonomy (~3 weeks)
  ├── Depends on: Phase A + B (multi-source routing requires complexity classification + iterative retrieval)
  ├── Soft dependency on Phase C (graph memory enhances but isn't required)
  ├── Delivers: RetrievalPlanStep, multi-source orchestration, Ragas CI/CD gates
  └── Impact: Planner uses retrieval, evaluation prevents quality regression
```

## Plan Documents

| Phase | Plan | Effort | Status |
|-------|------|--------|--------|
| A | [Phase A: Adaptive Routing](2026-05-19-agentic-rag-phase-a.md) | ~2 weeks | Ready |
| B | [Phase B: Multi-Hop & Reflection](2026-05-19-agentic-rag-phase-b.md) | ~3 weeks | Ready |
| C | [Phase C: Production Graph & Memory](2026-05-19-agentic-rag-phase-c.md) | ~4 weeks | Ready |
| D | [Phase D: Full Autonomy](2026-05-19-agentic-rag-phase-d.md) | ~3 weeks | Ready |

## Cross-Phase Conventions

- **Config namespace:** `AppConfig.AI.Rag.*` — each phase adds its section
- **DI pattern:** Keyed services via `AddKeyed{Lifetime}<T>(key, impl)` in `DependencyInjection.cs`
- **Test data:** Extend `RagTestData.cs` helper with builders for new types
- **Commit prefix:** `feat(rag):` for features, `test(rag):` for tests, `refactor(rag):` for restructuring
- **Verification:** `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx` after each task

## Cost Model

| Architecture | Relative Cost | Latency p50 | When |
|---|---|---|---|
| Current (CRAG every query) | 2-3x baseline | 3-5s | Now |
| After Phase A (adaptive routing) | 0.7-1.5x | 1-4s | +2w |
| After Phase B (multi-hop) | 1-3x (simple cheap, complex expensive) | 1-8s | +5w |
| After Phase C (graph + memory) | 1-3x (feedback improves over time) | 1-8s | +9w |
| After Phase D (full autonomy) | 0.5-5x (cost-optimized routing) | 1-12s | +12w |

## Risk Register

| Risk | Mitigation | Phase |
|---|---|---|
| LLM complexity classifier accuracy | Eval dataset + threshold tuning; fallback to moderate path | A |
| Multi-hop infinite loops | Hard cap on iterations (3 max); token budget enforcement | B |
| Neo4j operational complexity | Kuzu as embedded alternative; behind IKnowledgeGraphStore interface | C |
| Ragas eval flaky in CI | Deterministic test fixtures; threshold margin; skip on timeout | D |
| Cross-session memory staleness | EMA decay + explicit Forget; pruning cron | C |
