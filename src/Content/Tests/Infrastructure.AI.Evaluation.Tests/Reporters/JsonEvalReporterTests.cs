using System.Text;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Reporters;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Reporters;

public sealed class JsonEvalReporterTests
{
    [Fact]
    public void Key_IsJson() => new JsonEvalReporter().FormatKey.Should().Be("json");

    [Fact]
    public async Task Writes_deterministic_json_matching_golden_fixture()
    {
        var report = TestReportFactory.DeterministicReport();
        var reporter = new JsonEvalReporter();

        using var ms = new MemoryStream();
        await reporter.WriteAsync(report, ms, CancellationToken.None);
        var actual = Encoding.UTF8.GetString(ms.ToArray());

        // Persist actual alongside the test assembly for inspection / fixture regeneration.
        var actualPath = Path.Combine(AppContext.BaseDirectory, "actual-eval-report.json");
        await File.WriteAllTextAsync(actualPath, actual);

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Reports", "expected-eval-report.json");
        File.Exists(goldenPath).Should().BeTrue($"golden fixture missing at {goldenPath}; actual written to {actualPath}");

        var expected = await File.ReadAllTextAsync(goldenPath);
        Normalize(actual).Should().Be(Normalize(expected));
    }

    private static string Normalize(string s) =>
        s.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
