using FluentAssertions;
using Infrastructure.AI.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Prompts;

/// <summary>
/// Walks up from the test bin to find the repo-root <c>prompts/</c> folder, loads
/// each shipped template via <see cref="FilePromptRegistry"/>, and asserts the seed
/// (5 RAG judge prompts copied from 5.2 as v1) is reachable. Guards against silent
/// folder-rename drift and missing-template regressions.
/// </summary>
public sealed class SeedPromptsIntegrationTests
{
    private static string? LocatePromptsRoot()
    {
        // Anchor on the `.prompts-root` marker file so we don't match unrelated
        // `Prompts/` test-source directories on case-insensitive filesystems.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "prompts");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, ".prompts-root")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task Repo_prompts_folder_is_locatable_and_contains_the_5_shipped_judge_prompts()
    {
        var root = LocatePromptsRoot();
        root.Should().NotBeNull("repo-root prompts/ folder must be reachable from test bin");

        var sut = new FilePromptRegistry(root!, NullLogger<FilePromptRegistry>.Instance);
        var names = await sut.ListNamesAsync(CancellationToken.None);

        names.Should().Contain(new[]
        {
            "faithfulness-judge",
            "context-precision-judge",
            "context-recall-judge",
            "answer-relevance-judge",
            "answer-correctness-judge"
        });
    }

    [Theory]
    [InlineData("faithfulness-judge")]
    [InlineData("context-precision-judge")]
    [InlineData("context-recall-judge")]
    [InlineData("answer-relevance-judge")]
    [InlineData("answer-correctness-judge")]
    public async Task Each_seed_judge_prompt_loads_with_v1_and_non_empty_body(string name)
    {
        var root = LocatePromptsRoot();
        root.Should().NotBeNull();
        var sut = new FilePromptRegistry(root!, NullLogger<FilePromptRegistry>.Instance);

        var d = await sut.GetLatestAsync(name, CancellationToken.None);

        d.Version.Major.Should().Be(1);
        d.Body.Should().NotBeNullOrWhiteSpace();
        d.Body.Should().Contain("score", "every judge prompt instructs the model to emit a score");
        d.ContentHash.Should().NotBeNullOrWhiteSpace();
        d.Identifier.Should().StartWith($"{name}@v1");
    }
}
