namespace Domain.AI.SkillTraining;

/// <summary>
/// A named, governable surface of the agent harness that a skill-training patch could target.
/// </summary>
/// <remarks>
/// <para>
/// The self-optimization loop today edits exactly one surface — <see cref="SkillDocument"/> (a
/// SKILL.md prose file). Self-Harness (arXiv 2606.09498) points the same propose → evaluate → accept
/// loop at the <em>entire</em> harness. Before any such widening, every surface is named here so the
/// <c>EditableSurfaceRegistry</c> fence can declare — in code, not configuration — which surfaces a
/// patch may legally touch and which are frozen.
/// </para>
/// <para>
/// Surfaces fall into three intent bands. First, the single editable surface today
/// (<see cref="SkillDocument"/>). Second, low-stakes runtime-policy surfaces a future phase could
/// safely unfreeze. Third, governance surfaces that must remain <em>frozen by construction</em> —
/// <see cref="DeniedTools"/>, <see cref="AutonomyTier"/>, <see cref="ContentSafetyConfig"/>, and the
/// registry itself — which can never be marked editable, because an optimizer able to edit them could
/// weaken the very safety architecture it runs under.
/// </para>
/// </remarks>
public enum HarnessSurface
{
    /// <summary>The skill instruction document (SKILL.md prose). The only editable surface today.</summary>
    SkillDocument,

    /// <summary>The agent system prompt. Frozen pending a product decision (Self-Harness Phase 2).</summary>
    SystemPrompt,

    /// <summary>Guidance on how the agent produces artifacts. Low-stakes; Phase 2 candidate. Frozen today.</summary>
    ArtifactGuidance,

    /// <summary>Failure-recovery instructions. Low-stakes; Phase 2 candidate. Frozen today.</summary>
    FailureRecovery,

    /// <summary>Verification / self-check prompts. Low-stakes; Phase 2 candidate. Frozen today.</summary>
    VerificationPrompt,

    /// <summary>Tool-error retry / limit runtime policy. Low-stakes; Phase 2 candidate. Frozen today.</summary>
    ToolErrorRetryLimit,

    /// <summary>
    /// Tool availability — the agent's allowed tool set and tool declarations (AGENT.md). High-stakes;
    /// only ever editable behind a human escalation gate (Phase 3). Frozen today.
    /// </summary>
    ToolAvailability,

    /// <summary>Memory-scope and persistence rules. High-stakes; Phase 3 candidate. Frozen today.</summary>
    MemoryScopeRules,

    /// <summary>
    /// The bypass-immune denied-tool list. Frozen <b>by construction</b> — never editable by the loop,
    /// because re-enabling a denied tool is exactly what the denied-tool rule exists to prevent.
    /// </summary>
    DeniedTools,

    /// <summary>
    /// The agent autonomy tier (Restricted / Supervised / Autonomous). Frozen <b>by construction</b> —
    /// self-improvement must never be able to escalate its own autonomy.
    /// </summary>
    AutonomyTier,

    /// <summary>
    /// Content-safety and response-sanitization wiring. Frozen <b>by construction</b> — the loop must
    /// never be able to remove a safety filter to pass more tasks.
    /// </summary>
    ContentSafetyConfig,

    /// <summary>
    /// The editable-surface registry itself. Frozen <b>by construction</b> — the fence cannot be edited
    /// by the thing it fences.
    /// </summary>
    EditableSurfaceRegistry
}
