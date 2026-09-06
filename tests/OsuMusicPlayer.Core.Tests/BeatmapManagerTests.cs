using FluentAssertions;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Tests;

public sealed class BeatmapManagerTests
{
    [Fact]
    public async Task LoadAsync_MergesStableAndLazerSetsAndDifficulties()
    {
        var stable = ModelFactory.Set(100, source: BeatmapSource.Stable, beatmaps: [ModelFactory.Beatmap(1, "Easy")]);
        var lazer = ModelFactory.Set(100, source: BeatmapSource.Lazer, beatmaps: [ModelFactory.Beatmap(1, "Easy"), ModelFactory.Beatmap(2, "Hard")]);
        var manager = new BeatmapManager(
            [new FakeLoader(OsuInstallationKind.Stable, [stable]), new FakeLoader(OsuInstallationKind.Lazer, [lazer])],
            new DuplicateDetector());

        var result = await manager.LoadAsync(
            [new OsuInstallation(OsuInstallationKind.Stable, "stable"), new OsuInstallation(OsuInstallationKind.Lazer, "lazer")]);

        result.Should().ContainSingle();
        result[0].Source.Should().Be(BeatmapSource.Both);
        result[0].Beatmaps.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_KeepsBothHashesOfAMergedDifficulty()
    {
        var stable = ModelFactory.Set(100, source: BeatmapSource.Stable, beatmaps: [ModelFactory.Beatmap(1, "Easy") with { Md5Hash = "stable-hash" }]);
        var lazer = ModelFactory.Set(100, source: BeatmapSource.Lazer, beatmaps: [ModelFactory.Beatmap(1, "Easy") with { Md5Hash = "lazer-hash" }]);
        var manager = new BeatmapManager(
            [new FakeLoader(OsuInstallationKind.Stable, [stable]), new FakeLoader(OsuInstallationKind.Lazer, [lazer])],
            new DuplicateDetector());

        var result = await manager.LoadAsync(
            [new OsuInstallation(OsuInstallationKind.Stable, "stable"), new OsuInstallation(OsuInstallationKind.Lazer, "lazer")]);

        var merged = result.Should().ContainSingle().Which.Beatmaps.Should().ContainSingle().Subject;
        merged.Md5Hash.Should().Be("stable-hash");
        merged.AlternateMd5Hashes.Should().Equal("lazer-hash");
        merged.AllMd5Hashes.Should().Equal(new[] { "stable-hash", "lazer-hash" }, "a collection from either installation must still resolve");
    }

    [Fact]
    public async Task LoadAsync_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var manager = new BeatmapManager([], new DuplicateDetector());

        var action = () => manager.LoadAsync([], source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeLoader(OsuInstallationKind kind, IReadOnlyList<UnifiedBeatmapSet> sets) : IBeatmapLoader
    {
        public OsuInstallationKind Kind { get; } = kind;

        public Task<IReadOnlyList<UnifiedBeatmapSet>> LoadAsync(string installationPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(sets);
    }
}
