using Application.AI.Common.Services.Governance;
using Domain.AI.Governance;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Governance;

/// <summary>
/// Tests for <see cref="FileSystemAssetReferenceResolver"/>: it claims the <c>file_system</c> tool and
/// maps its <c>path</c> argument to a local-file asset, leaves other tools to the next resolver, and treats
/// a file_system call with no usable path as a claimed-but-unknown asset.
/// </summary>
public sealed class FileSystemAssetReferenceResolverTests
{
    private readonly FileSystemAssetReferenceResolver _resolver = new();

    [Fact]
    public void TryResolve_FileSystemToolWithPath_ResolvesLocalFile()
    {
        var args = new Dictionary<string, object?> { ["operation"] = "read", ["path"] = @"C:\data\notes.txt" };

        var claimed = _resolver.TryResolve("file_system", args, out var asset);

        claimed.Should().BeTrue();
        asset.Type.Should().Be(AssetType.LocalFile);
        asset.Identifier.Should().Be(@"C:\data\notes.txt");
    }

    [Fact]
    public void TryResolve_TrimsPath()
    {
        var args = new Dictionary<string, object?> { ["path"] = "  /etc/secrets.conf  " };

        _resolver.TryResolve("file_system", args, out var asset);

        asset.Identifier.Should().Be("/etc/secrets.conf");
    }

    [Fact]
    public void TryResolve_OtherTool_NotClaimed()
    {
        var args = new Dictionary<string, object?> { ["path"] = "x" };

        var claimed = _resolver.TryResolve("web_search", args, out var asset);

        claimed.Should().BeFalse("a non file_system tool is left for the next resolver");
        asset.Type.Should().Be(AssetType.Unknown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_FileSystemToolWithBlankPath_ClaimedAsUnknown(string blankPath)
    {
        var args = new Dictionary<string, object?> { ["path"] = blankPath };

        var claimed = _resolver.TryResolve("file_system", args, out var asset);

        claimed.Should().BeTrue("the file_system tool is ours even when the path is unusable");
        asset.Type.Should().Be(AssetType.Unknown);
    }

    [Fact]
    public void TryResolve_FileSystemToolWithMissingPath_ClaimedAsUnknown()
    {
        var args = new Dictionary<string, object?> { ["operation"] = "list" };

        var claimed = _resolver.TryResolve("file_system", args, out var asset);

        claimed.Should().BeTrue();
        asset.Type.Should().Be(AssetType.Unknown);
    }

    [Fact]
    public void TryResolve_PathNotAString_ClaimedAsUnknown()
    {
        var args = new Dictionary<string, object?> { ["path"] = 42 };

        var claimed = _resolver.TryResolve("file_system", args, out var asset);

        claimed.Should().BeTrue();
        asset.Type.Should().Be(AssetType.Unknown);
    }
}
