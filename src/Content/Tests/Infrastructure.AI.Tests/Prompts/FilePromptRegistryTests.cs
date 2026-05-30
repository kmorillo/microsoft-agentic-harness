using Domain.AI.Prompts;
using FluentAssertions;
using Infrastructure.AI.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Prompts;

public sealed class FilePromptRegistryTests : IDisposable
{
    private readonly string _root;

    public FilePromptRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "prompt-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private void WritePrompt(string name, string version, string body)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{version}.md"), body);
    }

    private FilePromptRegistry MakeSut() =>
        new(_root, NullLogger<FilePromptRegistry>.Instance);

    [Fact]
    public async Task ListAsync_returns_empty_when_name_unknown()
    {
        var sut = MakeSut();
        var result = await sut.ListAsync("nope", CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestAsync_throws_when_name_unknown()
    {
        var sut = MakeSut();
        Func<Task> act = () => sut.GetLatestAsync("nope", CancellationToken.None);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ListAsync_returns_versions_in_ascending_order()
    {
        WritePrompt("greet", "v2", "two");
        WritePrompt("greet", "v1", "one");
        WritePrompt("greet", "v1.5", "one-five");

        var sut = MakeSut();
        var all = await sut.ListAsync("greet", CancellationToken.None);

        all.Select(d => d.Version).Should().Equal(
            new PromptVersion(1, 0),
            new PromptVersion(1, 5),
            new PromptVersion(2, 0));
    }

    [Fact]
    public async Task GetLatestAsync_returns_highest_version()
    {
        WritePrompt("greet", "v1", "one");
        WritePrompt("greet", "v3.4", "three-four");
        WritePrompt("greet", "v2", "two");

        var sut = MakeSut();
        var latest = await sut.GetLatestAsync("greet", CancellationToken.None);

        latest.Version.Should().Be(new PromptVersion(3, 4));
        latest.Body.Should().Be("three-four");
    }

    [Fact]
    public async Task GetAsync_finds_exact_version_or_throws_listing_available()
    {
        WritePrompt("greet", "v1", "one");
        WritePrompt("greet", "v2", "two");

        var sut = MakeSut();

        var v1 = await sut.GetAsync("greet", new PromptVersion(1, 0), CancellationToken.None);
        v1.Body.Should().Be("one");

        Func<Task> act = () => sut.GetAsync("greet", new PromptVersion(9, 9), CancellationToken.None);
        var ex = await act.Should().ThrowAsync<KeyNotFoundException>();
        ex.Which.Message.Should().Contain("v1.0").And.Contain("v2.0");
    }

    [Fact]
    public async Task Descriptor_carries_sha256_content_hash_of_body()
    {
        WritePrompt("greet", "v1", "hello world");

        var sut = MakeSut();
        var d = await sut.GetLatestAsync("greet", CancellationToken.None);

        // SHA-256("hello world") = b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9
        d.ContentHash.Should().Be("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");
        d.Identifier.Should().Be("greet@v1.0");
    }

    [Fact]
    public async Task Frontmatter_is_stripped_and_parsed_into_metadata()
    {
        WritePrompt("greet", "v1",
            "---\n" +
            "description: hello prompt\n" +
            "owner: matt\n" +
            "---\n" +
            "Hi {{name}}\n");

        var sut = MakeSut();
        var d = await sut.GetLatestAsync("greet", CancellationToken.None);

        d.Body.Should().Be("Hi {{name}}\n");
        d.Metadata.Should().ContainKey("description").WhoseValue.Should().Be("hello prompt");
        d.Metadata.Should().ContainKey("owner").WhoseValue.Should().Be("matt");
    }

    [Fact]
    public async Task Interior_triple_dash_line_in_body_is_not_treated_as_close_fence()
    {
        WritePrompt("greet", "v1",
            "---\n" +
            "name: x\n" +
            "---\n" +
            "body line 1\n---\nbody line 2\n");

        var sut = MakeSut();
        var d = await sut.GetLatestAsync("greet", CancellationToken.None);

        d.Body.Should().Be("body line 1\n---\nbody line 2\n");
    }

    [Fact]
    public async Task Files_with_non_version_names_are_skipped_with_log()
    {
        var dir = Path.Combine(_root, "greet");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "v1.md"), "ok");
        File.WriteAllText(Path.Combine(dir, "vNOT-A-VERSION.md"), "ignored");
        File.WriteAllText(Path.Combine(dir, "README.md"), "also ignored — no v prefix");

        var sut = MakeSut();
        var list = await sut.ListAsync("greet", CancellationToken.None);

        list.Should().HaveCount(1);
        list[0].Version.Should().Be(new PromptVersion(1, 0));
    }

    [Fact]
    public async Task ListNamesAsync_enumerates_subdirectories()
    {
        WritePrompt("a", "v1", "a");
        WritePrompt("b", "v1", "b");
        WritePrompt("c", "v1", "c");

        var sut = MakeSut();
        var names = await sut.ListNamesAsync(CancellationToken.None);

        names.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public async Task ListNamesAsync_returns_empty_when_root_missing()
    {
        Directory.Delete(_root, recursive: true);

        var sut = MakeSut();
        var names = await sut.ListNamesAsync(CancellationToken.None);

        names.Should().BeEmpty();
    }
}
