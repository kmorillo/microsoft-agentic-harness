using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// The inert default <see cref="IHarnessChangeSuggester"/>: proposes nothing. With this registered (the
/// out-of-the-box state), the Phase 2 Step 2 suggestion path produces no suggestions even if a run opts
/// in via <c>TrainSkillConfig.EmitHarnessChangeSuggestions</c> — the feature is off until a consumer
/// plugs in a real suggester.
/// </summary>
/// <remarks>
/// This default <em>returns empty</em> rather than throwing, unlike <c>NotConfiguredPatchProposer</c> /
/// <c>NotConfiguredRolloutRunner</c>. Those seams are required for the loop to function at all, so a
/// missing implementation is a misconfiguration worth failing loudly on. The suggester is optional and
/// advisory: a run must complete normally whether or not a suggester is wired, so the safe default is
/// silence, not an exception.
/// </remarks>
public sealed class NoHarnessChangeSuggester : IHarnessChangeSuggester
{
    private static readonly IReadOnlyList<HarnessChangeSuggestion> None = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<HarnessChangeSuggestion>> SuggestAsync(
        HarnessSuggestionInput input,
        CancellationToken cancellationToken) => Task.FromResult(None);
}
