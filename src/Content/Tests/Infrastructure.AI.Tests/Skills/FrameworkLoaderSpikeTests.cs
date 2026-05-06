using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Skills;

/// <summary>
/// Read-only spike: verify that <see cref="FileAgentSkillsProvider"/> from
/// Microsoft.Agents.AI can be constructed with our existing skill paths.
/// <c>FileAgentSkillLoader</c> is internal — only <see cref="FileAgentSkillsProvider"/>
/// is publicly accessible for progressive skill disclosure.
/// </summary>
[Trait("Category", "Spike")]
public class FrameworkLoaderSpikeTests
{
    private readonly string _skillsRoot;

    public FrameworkLoaderSpikeTests()
    {
        // Walk up from the test bin folder to the repo root, then into the Skills directory.
        // AppContext.BaseDirectory = .../bin/Debug/net10.0/
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        _skillsRoot = Path.Combine(
            repoRoot,
            "src", "Content", "Application", "Application.Core", "Agents", "Skills");

        Directory.Exists(_skillsRoot).Should().BeTrue(
            $"Skills directory must exist at {_skillsRoot}");
    }

    [Fact]
    public void FileAgentSkillsProvider_ConstructionWithSkillPaths_DoesNotThrow()
    {
        var act = () => new FileAgentSkillsProvider(
            [_skillsRoot],
            options: null,
            loggerFactory: NullLoggerFactory.Instance);

        act.Should().NotThrow(
            "FileAgentSkillsProvider should accept our skill paths without error");
    }

    [Fact]
    public void FileAgentSkillsProvider_ConstructedInstance_IsNotNull()
    {
        var provider = new FileAgentSkillsProvider(
            [_skillsRoot],
            options: null,
            loggerFactory: NullLoggerFactory.Instance);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void FileAgentSkillsProvider_WithMultiplePaths_ConstructsSuccessfully()
    {
        // Point at individual skill directories
        var echoPath = Path.Combine(_skillsRoot, "echo-test");
        var orchestratorPath = Path.Combine(_skillsRoot, "orchestrator-agent");
        var researchPath = Path.Combine(_skillsRoot, "research-agent");

        var provider = new FileAgentSkillsProvider(
            [echoPath, orchestratorPath, researchPath],
            options: null,
            loggerFactory: NullLoggerFactory.Instance);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void FileAgentSkillsProvider_WithOptions_ConstructsSuccessfully()
    {
        var options = new FileAgentSkillsProviderOptions
        {
            SkillsInstructionPrompt = "Use load_skill to explore available skills."
        };

        var provider = new FileAgentSkillsProvider(
            [_skillsRoot],
            options: options,
            loggerFactory: NullLoggerFactory.Instance);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void FileAgentSkillsProvider_IsAIContextProvider()
    {
        var provider = new FileAgentSkillsProvider(
            [_skillsRoot],
            options: null,
            loggerFactory: NullLoggerFactory.Instance);

        provider.Should().BeAssignableTo<AIContextProvider>(
            "FileAgentSkillsProvider should be an AIContextProvider for agent wiring");
    }

    /// <summary>
    /// Walks up from the given directory until we find a directory containing AgenticHarness.slnx.
    /// </summary>
    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "src", "AgenticHarness.slnx")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not find repo root (containing src/AgenticHarness.slnx) starting from {startDir}");
    }
}
