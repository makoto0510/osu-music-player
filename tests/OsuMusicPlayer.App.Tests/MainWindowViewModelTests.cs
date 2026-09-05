using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SearchAndSort_FilterAllRequestedMetadata()
    {
        using var viewModel = createViewModel(out _);
        viewModel.ReplaceTracksForTesting([
            createSet("Zeta", "Alpha", "Mapper One", "electronic", 180, 100),
            createSet("Beta", "Omega", "Mapper Two", "rock featured", 120, 200),
        ]);

        viewModel.SearchText = "featured";
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("Beta");

        viewModel.SearchText = string.Empty;
        viewModel.SelectedSort = TrackSortOption.BPM;
        viewModel.Tracks.Select(static track => track.Title).Should().Equal("Zeta", "Beta");

        viewModel.SearchText = "mapper one";
        viewModel.Tracks.Should().ContainSingle().Which.Artist.Should().Be("Alpha");
    }

    [Theory]
    [InlineData(TrackSortOption.Title, "Beta", "Zeta")]
    [InlineData(TrackSortOption.Artist, "Zeta", "Beta")]
    [InlineData(TrackSortOption.BPM, "Zeta", "Beta")]
    [InlineData(TrackSortOption.Length, "Beta", "Zeta")]
    public void SortOptions_OrderTracks(TrackSortOption option, string first, string second)
    {
        using var viewModel = createViewModel(out _);
        viewModel.ReplaceTracksForTesting([
            createSet("Zeta", "Alpha", "Mapper", "", 180, 100),
            createSet("Beta", "Omega", "Mapper", "", 120, 200),
        ]);

        viewModel.SelectedSort = option;

        viewModel.Tracks.Select(static track => track.Title).Should().Equal(first, second);
    }

    [Fact]
    public async Task PlaybackCommands_LoadNavigateSeekAndApplySettings()
    {
        using var viewModel = createViewModel(out var audio);
        viewModel.ReplaceTracksForTesting([
            createSet("First", "Artist", "Mapper", "", 100, 100),
            createSet("Second", "Artist", "Mapper", "", 120, 200),
        ]);
        viewModel.SelectedTrack = viewModel.Tracks[0];

        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        viewModel.Volume = 0.35;
        viewModel.SetModCommand.Execute(OsuAudioMod.NC);
        viewModel.Progress = 0.5;
        await viewModel.NextCommand.ExecuteAsync(null);

        audio.LoadedPaths.Should().HaveCount(2);
        viewModel.CurrentTrack?.Title.Should().Be("Second");
        audio.Volume.Should().BeApproximately(0.35f, 0.001f);
        audio.Mod.Should().Be(OsuAudioMod.NC);
        audio.LastSeek.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task BrokenCodec_IsReportedWithoutEscapingCommand()
    {
        using var viewModel = createViewModel(out var audio);
        audio.LoadError = new AudioEngineException("bad codec");
        viewModel.ReplaceTracksForTesting([createSet("Broken", "Artist", "Mapper", "", 100, 100)]);
        viewModel.SelectedTrack = viewModel.Tracks[0];

        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("bad codec");
        viewModel.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task Initialize_WithoutInstallations_OpensSourcesPanelAndExplains()
    {
        using var viewModel = createViewModel(out _, out var environment);

        await viewModel.InitializeAsync();

        viewModel.HasInstallations.Should().BeFalse();
        viewModel.IsSourcesPanelVisible.Should().BeTrue();
        viewModel.EmptyStateText.Should().Contain("Sources");
        viewModel.IsLoading.Should().BeFalse();
        environment.Loader.LoadedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Initialize_RestoresManualInstallationsFromSettingsAndDropsInvalidOnes()
    {
        using var directory = new TestDirectory();
        var lazerPath = createLazerInstallation(directory, "lazer");
        using var viewModel = createViewModel(out _, out var environment);
        environment.Settings.Current = new AppSettings
        {
            ManualInstallations =
            [
                new ManualInstallationSetting(OsuInstallationKind.Lazer, lazerPath),
                new ManualInstallationSetting(OsuInstallationKind.Lazer, Path.Combine(directory.Path, "does-not-exist")),
            ],
        };
        environment.Loader.Sets = [createSet("Restored", "Artist", "Mapper", "", 150, 100)];

        await viewModel.InitializeAsync();

        viewModel.Installations.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Kind = OsuInstallationKind.Lazer, Path = lazerPath, IsManual = true });
        environment.Loader.LoadedPaths.Should().Equal(lazerPath);
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("Restored");
        viewModel.IsSourcesPanelVisible.Should().BeFalse();
        viewModel.LibraryStatusText.Should().Contain("1 track");
    }

    [Fact]
    public async Task AddLazerFolder_ValidatesPersistsAndReloadsLibrary()
    {
        using var directory = new TestDirectory();
        var lazerPath = createLazerInstallation(directory, "lazer");
        using var viewModel = createViewModel(out _, out var environment);
        await viewModel.InitializeAsync();
        environment.Picker.NextPath = lazerPath;
        environment.Loader.Sets = [createSet("Added", "Artist", "Mapper", "", 150, 100)];

        await viewModel.AddLazerFolderCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeFalse();
        viewModel.Installations.Should().ContainSingle().Which.IsManual.Should().BeTrue();
        environment.Settings.Saved.Should().ContainSingle().Which.ManualInstallations.Should().Equal(new ManualInstallationSetting(OsuInstallationKind.Lazer, lazerPath));
        environment.Loader.LoadedPaths.Should().Equal(lazerPath);
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("Added");
    }

    [Fact]
    public async Task AddLazerFolder_RejectsFolderWithoutRealmAndDoesNotPersist()
    {
        using var directory = new TestDirectory();
        var wrongPath = directory.CreateDirectory("not-osu");
        using var viewModel = createViewModel(out _, out var environment);
        await viewModel.InitializeAsync();
        environment.Picker.NextPath = wrongPath;

        await viewModel.AddLazerFolderCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("client.realm");
        viewModel.Installations.Should().BeEmpty();
        environment.Settings.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AddFolder_CancelledPicker_ChangesNothing()
    {
        using var viewModel = createViewModel(out _, out var environment);
        await viewModel.InitializeAsync();
        environment.Picker.NextPath = null;

        await viewModel.AddLazerFolderCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeFalse();
        viewModel.Installations.Should().BeEmpty();
        environment.Settings.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AddFolder_AlreadyKnownInstallation_IsNotDuplicated()
    {
        using var directory = new TestDirectory();
        var lazerPath = createLazerInstallation(directory, "lazer");
        using var viewModel = createViewModel(out _, out var environment);
        environment.Locator.Detected = [new OsuInstallation(OsuInstallationKind.Lazer, lazerPath)];
        await viewModel.InitializeAsync();
        environment.Picker.NextPath = lazerPath.ToUpperInvariant();

        await viewModel.AddLazerFolderCommand.ExecuteAsync(null);

        viewModel.Installations.Should().ContainSingle().Which.IsManual.Should().BeFalse();
        environment.Settings.Saved.Should().BeEmpty();
        viewModel.LibraryStatusText.Should().Contain("既に追加");
    }

    [Fact]
    public async Task RemoveInstallation_PersistsAndReloadsWhilePreservingCurrentTrack()
    {
        using var directory = new TestDirectory();
        var lazerPath = createLazerInstallation(directory, "lazer");
        var keptSet = createSet("Kept", "Artist", "Mapper", "", 150, 100);
        using var viewModel = createViewModel(out var audio, out var environment);
        environment.Locator.Detected = [new OsuInstallation(OsuInstallationKind.Lazer, Path.Combine(directory.Path, "auto"))];
        environment.Settings.Current = new AppSettings { ManualInstallations = [new ManualInstallationSetting(OsuInstallationKind.Lazer, lazerPath)] };
        environment.Loader.Sets = [keptSet, createSet("Removed", "Artist", "Mapper", "", 150, 100)];
        await viewModel.InitializeAsync();
        viewModel.SelectedTrack = viewModel.Tracks.Single(track => track.Title == "Kept");
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        var manual = viewModel.Installations.Single(item => item.IsManual);
        environment.Loader.Sets = [keptSet];

        await manual.RemoveCommand.ExecuteAsync(null);

        viewModel.Installations.Should().ContainSingle().Which.IsManual.Should().BeFalse();
        environment.Settings.Saved.Should().ContainSingle().Which.ManualInstallations.Should().BeEmpty();
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("Kept");
        viewModel.CurrentTrack.Should().BeSameAs(viewModel.Tracks[0], "playback keeps pointing at the re-created item");
        audio.LoadedPaths.Should().ContainSingle();
    }

    [Fact]
    public void AddStableFolder_IsOnlyAvailableOnWindows()
    {
        using var viewModel = createViewModel(out _);

        viewModel.CanAddStableFolder.Should().Be(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        viewModel.AddStableFolderCommand.CanExecute(null).Should().Be(viewModel.CanAddStableFolder);
    }

    [Fact]
    public async Task Queue_TakesPrecedenceOverSequentialOrderAndIsConsumed()
    {
        using var viewModel = createViewModel(out var audio);
        viewModel.ReplaceTracksForTesting([
            createSet("A", "Artist", "Mapper", "", 100, 100),
            createSet("B", "Artist", "Mapper", "", 100, 100),
            createSet("C", "Artist", "Mapper", "", 100, 100),
        ]);
        viewModel.SelectedTrack = viewModel.Tracks[0];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        viewModel.SelectedTrack = viewModel.Tracks[2];
        viewModel.EnqueueSelectedCommand.Execute(null);

        viewModel.QueueText.Should().Be("Queue (1)");
        audio.RaiseEnded();

        viewModel.CurrentTrack?.Title.Should().Be("C");
        viewModel.HasQueue.Should().BeFalse();
        audio.RaiseEnded();
        viewModel.CurrentTrack?.Title.Should().Be("C", "C is the last track and repeat is off, so playback stops there");
        viewModel.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task RepeatOff_StopsAtEndOfListButManualNextWraps()
    {
        using var viewModel = createViewModel(out var audio);
        viewModel.ReplaceTracksForTesting([
            createSet("A", "Artist", "Mapper", "", 100, 100),
            createSet("B", "Artist", "Mapper", "", 100, 100),
        ]);
        viewModel.SelectedTrack = viewModel.Tracks[1];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        audio.RaiseEnded();

        viewModel.CurrentTrack?.Title.Should().Be("B");
        viewModel.IsPlaying.Should().BeFalse();
        audio.LoadedPaths.Should().ContainSingle();

        await viewModel.NextCommand.ExecuteAsync(null);
        viewModel.CurrentTrack?.Title.Should().Be("A");
    }

    [Fact]
    public async Task RepeatAll_WrapsAutomaticallyAndRepeatOne_RestartsTrack()
    {
        using var viewModel = createViewModel(out var audio);
        viewModel.ReplaceTracksForTesting([
            createSet("A", "Artist", "Mapper", "", 100, 100),
            createSet("B", "Artist", "Mapper", "", 100, 100),
        ]);
        viewModel.SelectedTrack = viewModel.Tracks[1];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        viewModel.CycleRepeatCommand.Execute(null);
        viewModel.RepeatMode.Should().Be(RepeatMode.All);

        audio.RaiseEnded();
        viewModel.CurrentTrack?.Title.Should().Be("A");

        viewModel.CycleRepeatCommand.Execute(null);
        viewModel.RepeatText.Should().Be("Repeat: One");
        audio.Seek(TimeSpan.FromSeconds(30));
        var loadsBefore = audio.LoadedPaths.Count;
        audio.RaiseEnded();

        viewModel.CurrentTrack?.Title.Should().Be("A");
        audio.LastSeek.Should().Be(TimeSpan.Zero);
        audio.LoadedPaths.Should().HaveCount(loadsBefore);
        viewModel.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task Shuffle_AvoidsRepeatsUntilAllPlayedAndPreviousWalksHistory()
    {
        using var viewModel = createViewModel(out _);
        viewModel.ReplaceTracksForTesting([
            createSet("A", "Artist", "Mapper", "", 100, 100),
            createSet("B", "Artist", "Mapper", "", 100, 100),
            createSet("C", "Artist", "Mapper", "", 100, 100),
            createSet("D", "Artist", "Mapper", "", 100, 100),
        ]);
        viewModel.IsShuffleEnabled = true;
        viewModel.SelectedTrack = viewModel.Tracks[0];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        var played = new List<string> { "A" };
        for (var i = 0; i < 3; i++)
        {
            await viewModel.NextCommand.ExecuteAsync(null);
            played.Add(viewModel.CurrentTrack!.Title);
        }

        played.Should().OnlyHaveUniqueItems("a shuffle round plays every track once before repeating");
        await viewModel.PreviousCommand.ExecuteAsync(null);
        viewModel.CurrentTrack!.Title.Should().Be(played[^2]);
    }

    [Fact]
    public void Queue_DropsTracksThatDisappearOnReload()
    {
        using var viewModel = createViewModel(out _);
        var kept = createSet("Kept", "Artist", "Mapper", "", 100, 100);
        viewModel.ReplaceTracksForTesting([kept, createSet("Gone", "Artist", "Mapper", "", 100, 100)]);
        foreach (var track in viewModel.Tracks.ToArray())
        {
            viewModel.SelectedTrack = track;
            viewModel.EnqueueSelectedCommand.Execute(null);
        }

        viewModel.ReplaceTracksForTesting([kept]);

        viewModel.Queue.Should().ContainSingle().Which.Should().BeSameAs(viewModel.Tracks[0]);
        viewModel.ClearQueueCommand.Execute(null);
        viewModel.HasQueue.Should().BeFalse();
    }

    [Fact]
    public async Task PreviewSelected_StartsFromPreviewPointAndResolvesMedia()
    {
        using var viewModel = createViewModel(out var audio, out var environment);
        viewModel.ReplaceTracksForTesting([createSet("Song", "Artist", "Mapper", "", 100, 100)]);
        viewModel.SelectedTrack = viewModel.Tracks[0];
        environment.Media.Next = new BeatmapMedia(@"C:\video.mp4", TimeSpan.Zero, null);

        await viewModel.PreviewSelectedCommand.ExecuteAsync(null);

        audio.LoadedPaths.Should().ContainSingle();
        audio.LastSeek.Should().Be(TimeSpan.FromSeconds(40));
        viewModel.IsPlaying.Should().BeTrue();
        environment.Media.Requested.Should().Equal(viewModel.Tracks[0].Model.Id);
        viewModel.HasVideo.Should().BeTrue();
        viewModel.HasStoryboard.Should().BeFalse();
    }

    [Fact]
    public async Task PlaySelected_StartsFromBeginningAndClearsPreviousMedia()
    {
        using var viewModel = createViewModel(out var audio, out var environment);
        viewModel.ReplaceTracksForTesting([
            createSet("First", "Artist", "Mapper", "", 100, 100),
            createSet("Second", "Artist", "Mapper", "", 100, 100),
        ]);
        viewModel.SelectedTrack = viewModel.Tracks[0];
        environment.Media.Next = new BeatmapMedia(null, TimeSpan.Zero, @"C:\story.osb");
        await viewModel.PreviewSelectedCommand.ExecuteAsync(null);
        viewModel.HasStoryboard.Should().BeTrue();

        environment.Media.Next = BeatmapMedia.None;
        viewModel.SelectedTrack = viewModel.Tracks[1];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        audio.LastSeek.Should().Be(TimeSpan.FromSeconds(40), "a plain play does not seek, so the preview seek remains the last one");
        audio.LoadedPaths.Should().HaveCount(2);
        viewModel.HasStoryboard.Should().BeFalse();
    }

    [Fact]
    public async Task DetailTrack_FollowsSelectionThenPlayback()
    {
        using var viewModel = createViewModel(out _);
        viewModel.ReplaceTracksForTesting([
            createSet("First", "Artist", "Mapper", "", 100, 100),
            createSet("Second", "Artist", "Mapper", "", 100, 100),
        ]);
        var changes = new List<string>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName ?? string.Empty);

        viewModel.HasDetailTrack.Should().BeFalse();
        viewModel.SelectedTrack = viewModel.Tracks[1];
        viewModel.DetailTrack.Should().BeSameAs(viewModel.Tracks[1]);
        await viewModel.NextCommand.ExecuteAsync(null);
        viewModel.DetailTrack.Should().BeSameAs(viewModel.CurrentTrack);

        changes.Should().Contain(nameof(MainWindowViewModel.DetailTrack));
        viewModel.DetailTrack!.Difficulties.Select(static difficulty => difficulty.Name).Should().Equal("Normal", "Insane");
        viewModel.DetailTrack.StarText.Should().Be("up to 5.50★");
        viewModel.DetailTrack.DifficultyCountText.Should().Be("2 difficulties");
    }

    [Fact]
    public async Task Video_FollowsTrackPausePlaySeekAndMods()
    {
        using var viewModel = createViewModel(out var audio, out var environment);
        viewModel.ReplaceTracksForTesting([
            createSet("WithVideo", "Artist", "Mapper", "", 100, 100),
            createSet("Plain", "Artist", "Mapper", "", 100, 100),
        ]);
        viewModel.SelectedTrack = viewModel.Tracks[1];
        environment.Media.Next = new BeatmapMedia(@"C:\clip.mp4", TimeSpan.FromSeconds(2), null);

        await viewModel.PreviewSelectedCommand.ExecuteAsync(null);

        viewModel.IsVideoVisible.Should().BeTrue();
        environment.Video.CurrentPath.Should().Be(@"C:\clip.mp4");
        environment.Video.LoadedStart.Should().Be(TimeSpan.FromSeconds(38), "audio starts at the 40 s preview point and the video is offset by 2 s");
        environment.Video.IsPaused.Should().BeFalse();

        viewModel.TogglePlayCommand.Execute(null);
        environment.Video.IsPaused.Should().BeTrue();
        viewModel.TogglePlayCommand.Execute(null);
        environment.Video.IsPaused.Should().BeFalse();

        viewModel.Progress = 0.5;
        environment.Video.LastSeek.Should().Be(TimeSpan.FromSeconds(58));

        viewModel.SetModCommand.Execute(OsuAudioMod.DT);
        environment.Video.Rate.Should().Be(1.5);
        viewModel.SetModCommand.Execute(OsuAudioMod.DT);
        environment.Video.Rate.Should().Be(1);

        environment.Media.Next = BeatmapMedia.None;
        viewModel.SelectedTrack = viewModel.Tracks[0];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        environment.Video.CurrentPath.Should().BeNull("a track without video stops the previous video");
        viewModel.IsVideoVisible.Should().BeFalse();
        audio.LoadedPaths.Should().HaveCount(2);
    }

    [Fact]
    public async Task Video_ToggleStopsAndRestarts_AndUnavailablePlayerNeverLoads()
    {
        using var viewModel = createViewModel(out _, out var environment);
        viewModel.ReplaceTracksForTesting([createSet("WithVideo", "Artist", "Mapper", "", 100, 100)]);
        viewModel.SelectedTrack = viewModel.Tracks[0];
        environment.Media.Next = new BeatmapMedia(@"C:\clip.mp4", TimeSpan.Zero, null);
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        environment.Video.Loads.Should().Be(1);

        viewModel.IsVideoEnabled = false;
        environment.Video.CurrentPath.Should().BeNull();
        viewModel.IsVideoVisible.Should().BeFalse();
        viewModel.IsVideoEnabled = true;
        environment.Video.Loads.Should().Be(2);
        viewModel.IsVideoVisible.Should().BeTrue();

        environment.Video.IsAvailable = false;
        viewModel.IsVideoVisible.Should().BeFalse();
        environment.Video.Stop();
        viewModel.SelectedTrack = viewModel.Tracks[0];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        environment.Video.Loads.Should().Be(2, "an unavailable player is never asked to load");
    }

    [Fact]
    public void Search_RequiresEveryWordToMatchSomeField()
    {
        using var viewModel = createViewModel(out _);
        viewModel.ReplaceTracksForTesting([
            createSet("Zero Centimeters", "Ohara Yuiko", "-Mikan", "", 154, 100),
            createSet("Zero Centimeters", "Ohara Yuiko", "realy0_", "", 154, 100),
            createSet("Other", "Artist", "-Mikan", "", 100, 100),
        ]);

        viewModel.SearchText = "zero mikan";
        viewModel.Tracks.Should().ContainSingle().Which.Creator.Should().Be("-Mikan");

        viewModel.SearchText = "  centimeters   ";
        viewModel.Tracks.Should().HaveCount(2);
    }

    private static string createLazerInstallation(TestDirectory directory, string name)
    {
        directory.CreateFile(Path.Combine(name, "client.realm"));
        return directory.CreateDirectory(name, "files") is { } _ ? Path.GetFullPath(Path.Combine(directory.Path, name)) : throw new InvalidOperationException();
    }

    private static MainWindowViewModel createViewModel(out FakeAudioEngine audio) => createViewModel(out audio, out _);

    private static MainWindowViewModel createViewModel(out FakeAudioEngine audio, out TestEnvironment environment)
    {
        audio = new FakeAudioEngine();
        environment = new TestEnvironment();
        var manager = new BeatmapManager([environment.Loader], new DuplicateDetector());
        return new MainWindowViewModel(manager, audio, environment.Locator, new ImmediateDispatcher(), new NullImageLoader(), environment.Settings, environment.Picker, environment.Media, environment.Video);
    }

    private static UnifiedBeatmapSet createSet(string title, string artist, string creator, string tags, double bpm, int seconds) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        TitleUnicode = string.Empty,
        Artist = artist,
        ArtistUnicode = string.Empty,
        Creator = creator,
        AudioFilePath = $"{title}.mp3",
        Source = BeatmapSource.Stable,
        Beatmaps =
        [
            new UnifiedBeatmap
            {
                Id = Guid.NewGuid(),
                DifficultyName = "Normal",
                StarRating = 2.5,
                BPM = bpm,
                Length = TimeSpan.FromSeconds(seconds),
                PreviewTime = TimeSpan.FromSeconds(seconds * 0.4),
                Tags = tags,
            },
            new UnifiedBeatmap
            {
                Id = Guid.NewGuid(),
                DifficultyName = "Insane",
                StarRating = 5.5,
                BPM = bpm,
                Length = TimeSpan.FromSeconds(seconds),
                PreviewTime = TimeSpan.FromSeconds(seconds * 0.4),
                Tags = tags,
            },
        ],
    };

    private sealed class TestEnvironment
    {
        public FakeLoader Loader { get; } = new();
        public FakeLocator Locator { get; } = new();
        public FakeSettingsStore Settings { get; } = new();
        public FakeFolderPicker Picker { get; } = new();
        public FakeMediaResolver Media { get; } = new();
        public FakeVideoPlayer Video { get; } = new();
    }

    private sealed class FakeVideoPlayer : IVideoPlayer
    {
        public bool IsAvailable { get; set; } = true;
        public string? UnavailableReason => IsAvailable ? null : "no libvlc";
        public object? Surface => null;
        public string? CurrentPath { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan LoadedStart { get; private set; }
        public TimeSpan? LastSeek { get; private set; }
        public double Rate { get; private set; } = 1;
        public int Loads { get; private set; }
        public int Stops { get; private set; }
        public bool IsPaused { get; private set; }

        public void Load(string path, TimeSpan startAt) { CurrentPath = path; LoadedStart = startAt; Loads++; IsPaused = false; Position = startAt; }
        public void Play() => IsPaused = false;
        public void Pause() => IsPaused = true;
        public void Stop() { if (CurrentPath is not null) { Stops++; } CurrentPath = null; }
        public void Seek(TimeSpan position) { LastSeek = position; Position = position; }
        public void SetRate(double rate) => Rate = rate;
        public void Dispose() { }
    }

    private sealed class FakeMediaResolver : IBeatmapMediaResolver
    {
        public BeatmapMedia Next { get; set; } = BeatmapMedia.None;
        public List<Guid> Requested { get; } = [];

        public Task<BeatmapMedia> ResolveAsync(UnifiedBeatmapSet set, CancellationToken cancellationToken = default)
        {
            Requested.Add(set.Id);
            return Task.FromResult(Next);
        }
    }

    private sealed class FakeLoader : IBeatmapLoader
    {
        public OsuInstallationKind Kind => OsuInstallationKind.Lazer;
        public IReadOnlyList<UnifiedBeatmapSet> Sets { get; set; } = [];
        public List<string> LoadedPaths { get; } = [];

        public Task<IReadOnlyList<UnifiedBeatmapSet>> LoadAsync(string installationPath, CancellationToken cancellationToken = default)
        {
            LoadedPaths.Add(installationPath);
            return Task.FromResult(Sets);
        }
    }

    private sealed class FakeLocator : IOsuInstallationLocator
    {
        private readonly OsuInstallationLocator real = new();

        public IReadOnlyList<OsuInstallation> Detected { get; set; } = [];

        public IReadOnlyList<OsuInstallation> FindInstallations() => Detected;

        public OsuInstallation? ValidateManualPath(string path, OsuInstallationKind kind) => real.ValidateManualPath(path, kind);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Current { get; set; } = new();
        public List<AppSettings> Saved { get; } = [];

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Saved.Add(settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFolderPicker : IFolderPicker
    {
        public string? NextPath { get; set; }

        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(NextPath);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }

    private sealed class NullImageLoader : IBackgroundImageLoader
    {
        public Task<Bitmap?> LoadAsync(string? path, CancellationToken cancellationToken = default, int decodeHeight = 96) => Task.FromResult<Bitmap?>(null);
    }

    private sealed class FakeAudioEngine : IAudioEngine
    {
        public event EventHandler? PlaybackEnded;
        public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
        public AudioPlaybackState State { get; private set; }
        public TimeSpan CurrentTime { get; private set; }
        public TimeSpan TotalTime { get; private set; } = TimeSpan.FromSeconds(120);
        public float Volume { get; set; } = 1;
        public OsuAudioMod Mod { get; set; }
        public List<string> LoadedPaths { get; } = [];
        public Exception? LoadError { get; set; }
        public TimeSpan LastSeek { get; private set; }

        public Task LoadAsync(string audioFilePath)
        {
            if (LoadError is not null)
            {
                return Task.FromException(LoadError);
            }

            LoadedPaths.Add(audioFilePath);
            CurrentTime = TimeSpan.Zero;
            return Task.CompletedTask;
        }

        public void Play() => State = AudioPlaybackState.Playing;
        public void Pause() => State = AudioPlaybackState.Paused;
        public void Stop() { State = AudioPlaybackState.Stopped; CurrentTime = TimeSpan.Zero; }
        public void TogglePlay() { if (State == AudioPlaybackState.Playing) Pause(); else Play(); }
        public void Seek(TimeSpan position) { LastSeek = position; CurrentTime = position; PositionChanged?.Invoke(this, new(position, TotalTime)); }
        public void Dispose() { }
        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }
}
