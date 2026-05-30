using FluentAssertions;
using Infrastructure.AI.Evaluation.Loaders;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Loaders;

public sealed class YamlEvalDatasetLoaderTests
{
    private readonly YamlEvalDatasetLoader _sut = new();

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Extensions_IncludesYamlAndYml()
    {
        _sut.Extensions.Should().Contain("yaml").And.Contain("yml");
    }

    [Fact]
    public async Task LoadAsync_ValidFile_ReturnsDatasetWithName()
    {
        var dataset = await _sut.LoadAsync(FixturePath("valid-dataset.yaml"), CancellationToken.None);

        dataset.Name.Should().Be("governance-sanitization");
        dataset.Version.Should().Be("1.2.0");
        dataset.Description.Should().Contain("PII redaction");
        dataset.SourcePath.Should().EndWith("valid-dataset.yaml");
    }

    [Fact]
    public async Task LoadAsync_ValidFile_ParsesAllCases()
    {
        var dataset = await _sut.LoadAsync(FixturePath("valid-dataset.yaml"), CancellationToken.None);

        dataset.Cases.Should().HaveCount(2);
        dataset.Cases[0].Id.Should().Be("gov-san-01");
        dataset.Cases[0].Input.Should().Contain("SSN 123-45-6789");
        dataset.Cases[0].Tags.Should().BeEquivalentTo("governance", "pii", "sanitization");
        dataset.Cases[0].MetricSpecs.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_ValidFile_ParsesMetricSpecParameters()
    {
        var dataset = await _sut.LoadAsync(FixturePath("valid-dataset.yaml"), CancellationToken.None);

        var firstMetric = dataset.Cases[0].MetricSpecs[0];
        firstMetric.MetricKey.Should().Be("does_not_contain");
        firstMetric.Threshold.Should().Be(1.0);
        firstMetric.Parameters.Should().ContainKey("values");
        firstMetric.Parameters["values"].Should().Be("123-45-6789|ssn");

        var secondMetric = dataset.Cases[0].MetricSpecs[1];
        secondMetric.MetricKey.Should().Be("llm_judge");
        secondMetric.Threshold.Should().Be(0.8);
        secondMetric.Parameters["rubric"].Should().StartWith("Did the assistant");
    }

    [Fact]
    public async Task LoadAsync_ValidFile_ParsesInvocationOverrides()
    {
        var dataset = await _sut.LoadAsync(FixturePath("valid-dataset.yaml"), CancellationToken.None);

        dataset.Cases[1].InvocationOverrides.Should().ContainKey("temperature");
        dataset.Cases[1].InvocationOverrides["temperature"].Should().Be("0.0");
    }

    [Fact]
    public async Task LoadAsync_EmptyCases_ReturnsDatasetWithZeroCases()
    {
        var dataset = await _sut.LoadAsync(FixturePath("empty-cases.yaml"), CancellationToken.None);

        dataset.Name.Should().Be("empty-dataset");
        dataset.Cases.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_NonExistentPath_ThrowsFileNotFound()
    {
        var act = async () => await _sut.LoadAsync(FixturePath("does-not-exist.yaml"), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadAsync_MalformedYaml_ThrowsInvalidData()
    {
        var act = async () => await _sut.LoadAsync(FixturePath("malformed.yaml"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }
}
