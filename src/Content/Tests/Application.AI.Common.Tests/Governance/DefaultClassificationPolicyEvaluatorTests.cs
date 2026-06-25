using Application.AI.Common.Services.Governance;
using Domain.AI.Governance;
using Domain.Common.Config.AI.Governance;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Governance;

/// <summary>
/// Tests for <see cref="DefaultClassificationPolicyEvaluator"/> — the pure projection of a resolved
/// <see cref="AssetLabelResult"/> onto a <see cref="ClassificationPolicyDecision"/>. Covers the three
/// precedence branches (unknown → label-mapped → default), case-insensitive label matching, action
/// fidelity, and rule/label provenance for audit.
/// </summary>
public class DefaultClassificationPolicyEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly DefaultClassificationPolicyEvaluator _evaluator = new();

    private static AssetReference Asset(AssetType type = AssetType.AzureBlob) =>
        new(type, "https://acct.blob.core.windows.net/c/secret.csv");

    private static AssetLabelResult Labelled(string labelName, LabelSource source = LabelSource.DataMap) =>
        new(Asset(), new SensitivityLabel("id-1", labelName), [], source, Now);

    [Fact]
    public void Evaluate_NoLabelUnknownSource_AppliesUnknownAssetAction()
    {
        var result = AssetLabelResult.Unknown(Asset(AssetType.Unknown), Now);
        var config = new DataClassificationConfig { UnknownAssetAction = ClassificationAction.Block };

        var decision = _evaluator.Evaluate(result, config);

        decision.Action.Should().Be(ClassificationAction.Block);
        decision.MatchedLabel.Should().BeNull();
        decision.MatchedRule.Should().Be(nameof(config.UnknownAssetAction));
    }

    [Fact]
    public void Evaluate_NoLabelUnknownSource_DefaultUnknownActionAllows()
    {
        // The template default: unknown assets are allowed (observe, don't break the agent).
        var result = AssetLabelResult.Unknown(Asset(AssetType.LocalFile), Now);

        var decision = _evaluator.Evaluate(result, new DataClassificationConfig());

        decision.Action.Should().Be(ClassificationAction.Allow);
    }

    [Fact]
    public void Evaluate_ClassifiedButNoLabel_AppliesUnknownAssetActionWithDistinctReason()
    {
        // Source is DataMap and classifications exist, but no overall sensitivity label was assigned.
        var result = new AssetLabelResult(
            Asset(), Label: null, [new DataClassification("Credit Card Number", 0.9)], LabelSource.DataMap, Now);
        var config = new DataClassificationConfig { UnknownAssetAction = ClassificationAction.Redact };

        var decision = _evaluator.Evaluate(result, config);

        decision.Action.Should().Be(ClassificationAction.Redact);
        decision.MatchedRule.Should().Be(nameof(config.UnknownAssetAction));
        decision.Reason.Should().Contain("no sensitivity label");
    }

    [Fact]
    public void Evaluate_MappedLabel_AppliesMappedActionAndRecordsRule()
    {
        var config = new DataClassificationConfig
        {
            Mode = ClassificationEnforcementMode.Enforce,
            LabelActions = new() { ["Confidential"] = ClassificationAction.Block },
        };

        var decision = _evaluator.Evaluate(Labelled("Confidential"), config);

        decision.Action.Should().Be(ClassificationAction.Block);
        decision.MatchedLabel.Should().Be("Confidential");
        decision.MatchedRule.Should().Be("LabelActions[Confidential]");
    }

    [Fact]
    public void Evaluate_UnmappedLabel_AppliesDefaultAction()
    {
        var config = new DataClassificationConfig
        {
            DefaultAction = ClassificationAction.Redact,
            LabelActions = new() { ["Confidential"] = ClassificationAction.Block },
        };

        var decision = _evaluator.Evaluate(Labelled("Public"), config);

        decision.Action.Should().Be(ClassificationAction.Redact);
        decision.MatchedLabel.Should().Be("Public");
        decision.MatchedRule.Should().Be(nameof(config.DefaultAction));
    }

    [Theory]
    [InlineData("confidential")]
    [InlineData("CONFIDENTIAL")]
    [InlineData("Confidential")]
    public void Evaluate_LabelMatch_IsCaseInsensitive(string ruleKey)
    {
        // Guards the IConfiguration-binding gotcha: matching must not depend on the dictionary comparer.
        var config = new DataClassificationConfig
        {
            LabelActions = new(StringComparer.Ordinal) { [ruleKey] = ClassificationAction.Block },
        };

        var decision = _evaluator.Evaluate(Labelled("Confidential"), config);

        decision.Action.Should().Be(ClassificationAction.Block);
    }

    [Fact]
    public void Evaluate_NullArguments_Throw()
    {
        var config = new DataClassificationConfig();

        _evaluator.Invoking(e => e.Evaluate(null!, config)).Should().Throw<ArgumentNullException>();
        _evaluator.Invoking(e => e.Evaluate(AssetLabelResult.Unknown(Asset(), Now), null!))
            .Should().Throw<ArgumentNullException>();
    }
}
