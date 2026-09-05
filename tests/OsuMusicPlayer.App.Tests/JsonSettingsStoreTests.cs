using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Core;

namespace OsuMusicPlayer.App.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenFileIsMissing()
    {
        using var directory = new TestDirectory();
        var store = new JsonSettingsStore(Path.Combine(directory.Path, "settings.json"));

        var settings = await store.LoadAsync();

        settings.ManualInstallations.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_RoundTripsManualInstallationsAndCreatesDirectory()
    {
        using var directory = new TestDirectory();
        var filePath = Path.Combine(directory.Path, "nested", "settings.json");
        var store = new JsonSettingsStore(filePath);
        var settings = new AppSettings
        {
            ManualInstallations =
            [
                new ManualInstallationSetting(OsuInstallationKind.Lazer, @"D:\osu-lazer"),
                new ManualInstallationSetting(OsuInstallationKind.Stable, @"D:\osu!"),
            ],
        };

        await store.SaveAsync(settings);
        var loaded = await new JsonSettingsStore(filePath).LoadAsync();

        File.Exists(filePath).Should().BeTrue();
        File.Exists(filePath + ".tmp").Should().BeFalse();
        loaded.ManualInstallations.Should().Equal(settings.ManualInstallations);
        (await File.ReadAllTextAsync(filePath)).Should().Contain("\"Lazer\"", "enums are stored by name so the file stays readable");
    }

    [Fact]
    public async Task LoadAsync_IgnoresCorruptFile()
    {
        using var directory = new TestDirectory();
        var filePath = directory.CreateFile("settings.json", "{ this is not json");
        var store = new JsonSettingsStore(filePath);

        var settings = await store.LoadAsync();

        settings.ManualInstallations.Should().BeEmpty();
    }

    [Fact]
    public void GetDefaultFilePath_IsInsideApplicationDataNotInsideOsu()
    {
        var path = JsonSettingsStore.GetDefaultFilePath();

        path.Should().EndWith(Path.Combine("OsuMusicPlayer", "settings.json"));
        path.Should().NotContainEquivalentOf(Path.Combine("osu", "files"));
    }
}
