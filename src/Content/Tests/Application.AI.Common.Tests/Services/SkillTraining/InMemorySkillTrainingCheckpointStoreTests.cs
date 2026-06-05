using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class InMemorySkillTrainingCheckpointStoreTests
{
    private static SkillTrainingCheckpoint Cp(string run, int step, double score, GateAction action = GateAction.Accept)
        => new()
        {
            RunId = run,
            SkillId = "skill-x",
            Step = step,
            Epoch = 1,
            SkillContent = $"v{step}",
            SkillHash = step.ToString("x64"),
            Score = score,
            Action = action,
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task Get_ReturnsSavedCheckpoint()
    {
        var sut = new InMemorySkillTrainingCheckpointStore();
        await sut.SaveAsync(Cp("r1", 3, 0.7), default);

        var loaded = await sut.GetAsync("r1", 3, default);

        loaded!.SkillContent.Should().Be("v3");
    }

    [Fact]
    public async Task Get_UnknownStep_ReturnsNull()
    {
        var sut = new InMemorySkillTrainingCheckpointStore();
        (await sut.GetAsync("r1", 99, default)).Should().BeNull();
    }

    [Fact]
    public async Task Save_OverwritesAtSameStep()
    {
        var sut = new InMemorySkillTrainingCheckpointStore();
        await sut.SaveAsync(Cp("r1", 3, 0.5), default);
        await sut.SaveAsync(Cp("r1", 3, 0.8), default);

        (await sut.GetAsync("r1", 3, default))!.Score.Should().Be(0.8);
    }

    [Fact]
    public async Task GetBest_ReturnsHighestScoring_TiesBrokenByLaterStep()
    {
        var sut = new InMemorySkillTrainingCheckpointStore();
        await sut.SaveAsync(Cp("r1", 1, 0.4), default);
        await sut.SaveAsync(Cp("r1", 2, 0.8), default);
        await sut.SaveAsync(Cp("r1", 3, 0.6), default);
        await sut.SaveAsync(Cp("r1", 4, 0.8), default);   // tie with step 2

        (await sut.GetBestAsync("r1", default))!.Step.Should().Be(4);
    }

    [Fact]
    public async Task GetBest_EmptyRun_ReturnsNull()
    {
        var sut = new InMemorySkillTrainingCheckpointStore();
        (await sut.GetBestAsync("nope", default)).Should().BeNull();
    }

    [Fact]
    public async Task List_ReturnsCheckpointsInStepOrder()
    {
        var sut = new InMemorySkillTrainingCheckpointStore();
        await sut.SaveAsync(Cp("r1", 3, 0.3), default);
        await sut.SaveAsync(Cp("r1", 1, 0.1), default);
        await sut.SaveAsync(Cp("r1", 2, 0.2), default);

        var list = await sut.ListAsync("r1", default);

        list.Select(c => c.Step).Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task Runs_AreIsolated()
    {
        var sut = new InMemorySkillTrainingCheckpointStore();
        await sut.SaveAsync(Cp("r1", 1, 0.5), default);
        await sut.SaveAsync(Cp("r2", 1, 0.9), default);

        (await sut.GetBestAsync("r1", default))!.Score.Should().Be(0.5);
        (await sut.GetBestAsync("r2", default))!.Score.Should().Be(0.9);
    }
}
