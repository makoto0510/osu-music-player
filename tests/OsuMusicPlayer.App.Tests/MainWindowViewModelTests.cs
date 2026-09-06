using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Hitsounds;
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

    [Fact]
    public async Task Hitsounds_LoadTimelineOfSelectedDifficultyAndFollowDifficultyChanges()
    {
        using var directory = new TestDirectory();
        var easy = directory.CreateFile("easy.osu", beatmapText(1));
        var hard = directory.CreateFile("hard.osu", beatmapText(3));
        using var viewModel = createViewModel(out _, out var environment);
        var set = createSet("Song", "Artist", "Mapper", "", 100, 100) with
        {
            Beatmaps =
            [
                new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "Easy", Tags = "", StarRating = 1.5, BeatmapFilePath = easy },
                new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "Hard", Tags = "", StarRating = 5, BeatmapFilePath = hard },
            ],
        };
        viewModel.ReplaceTracksForTesting([set]);
        viewModel.SelectedTrack = viewModel.Tracks[0];
        viewModel.IsHitsoundEnabled = true;
        environment.Hitsounds.IsEnabled.Should().BeTrue();

        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        await viewModel.LastVisualsLoad;

        viewModel.CurrentTrack!.SelectedDifficulty!.Name.Should().Be("Hard", "the highest rated difficulty is the default");
        viewModel.VisualsStatusText.Should().Be("3 hit sounds");
        environment.Hitsounds.Loads.Should().Equal(3);
        viewModel.VisualsStatusText.Should().Contain("3 hit sounds");

        viewModel.CurrentTrack.SelectedDifficulty = viewModel.CurrentTrack.Difficulties.Single(static difficulty => difficulty.Name == "Easy");
        await viewModel.LastVisualsLoad;

        environment.Hitsounds.Loads.Should().Equal(3, 1);
        viewModel.IsHitsoundEnabled = false;
        environment.Hitsounds.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Storyboard_LoadsWhenTheMediaHasOneAndIsClearedForTheNextTrack()
    {
        using var directory = new TestDirectory();
        using var viewModel = createViewModel(out _, out var environment);
        var withStoryboard = createSet("A Song", "Artist", "Mapper", "", 100, 100) with { Files = new FolderFileResolver(directory.Path) };
        viewModel.ReplaceTracksForTesting([withStoryboard, createSet("B Other", "Artist", "Mapper", "", 100, 100) with { Files = new FolderFileResolver(directory.Path) }]);
        environment.Media.Next = new BeatmapMedia(null, TimeSpan.Zero, null) { BeatmapHasStoryboardElements = true };
        environment.Storyboard.Next = static () => new StoryboardSession([], new Dictionary<string, SkiaSharp.SKImage?>(), widescreen: true);
        viewModel.SelectedTrack = viewModel.Tracks[0];

        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        await viewModel.LastVisualsLoad;

        viewModel.StoryboardSession.Should().NotBeNull();
        viewModel.IsStoryboardVisible.Should().BeTrue();
        viewModel.StoryboardPanelHeight.Should().Be(202);
        viewModel.VisualsStatusText.Should().Contain("storyboard");

        viewModel.IsStoryboardEnabled = false;
        viewModel.IsStoryboardVisible.Should().BeFalse();
        viewModel.StoryboardPanelHeight.Should().Be(0);

        environment.Media.Next = BeatmapMedia.None;
        await viewModel.NextCommand.ExecuteAsync(null);
        await viewModel.LastVisualsLoad;

        viewModel.StoryboardSession.Should().BeNull("the next track has no storyboard");
        environment.Storyboard.Loads.Should().Be(1);
    }

    private static string beatmapText(int circles)
    {
        var lines = new List<string>
        {
            "osu file format v14",
            "[General]",
            "AudioFilename: audio.mp3",
            "SampleSet: Normal",
            "Mode: 0",
            "[Metadata]",
            "Title:Song",
            "Artist:Artist",
            "[Difficulty]",
            "HPDrainRate:5",
            "CircleSize:4",
            "OverallDifficulty:7",
            "ApproachRate:9",
            "SliderMultiplier:1",
            "SliderTickRate:1",
            "[Events]",
            "[TimingPoints]",
            "0,500,4,1,0,100,1,0",
            "[HitObjects]",
        };
        for (var i = 0; i < circles; i++)
        {
            lines.Add($"100,100,{1000 + i * 500},1,0,0:0:0:0:");
        }

        return string.Join(Environment.NewLine, lines);
    }

    [Fact]
    public async Task Favourites_ToggleFilterAndPersist()
    {
        using var viewModel = createViewModel(out _, out var environment);
        await viewModel.InitializeAsync();
        viewModel.ReplaceTracksForTesting([createSet("A", "Artist", "Mapper", "", 100, 100), createSet("B", "Artist", "Mapper", "", 100, 100)]);
        viewModel.SelectedTrack = viewModel.Tracks[1];

        viewModel.ToggleFavouriteCommand.Execute(null);

        viewModel.IsDetailFavourite.Should().BeTrue();
        viewModel.Tracks[1].FavouriteMark.Should().Be("♥");
        viewModel.Views.Single(static view => view.Kind == LibraryViewKind.Favourites).Count.Should().Be(1);
        viewModel.SelectedView = viewModel.Views.Single(static view => view.Kind == LibraryViewKind.Favourites);
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("B");

        viewModel.BuildSettings().Favourites.Should().Equal(viewModel.Tracks[0].Model.Id);
        await viewModel.PendingSave;
        environment.Settings.Current.Favourites.Should().HaveCount(1);
    }

    [Fact]
    public async Task Playlists_CreateAddReorderRemoveAndRename()
    {
        using var viewModel = createViewModel(out _, out var environment);
        await viewModel.InitializeAsync();
        viewModel.ReplaceTracksForTesting([
            createSet("A", "Artist", "Mapper", "", 100, 100),
            createSet("B", "Artist", "Mapper", "", 100, 100),
            createSet("C", "Artist", "Mapper", "", 100, 100),
        ]);

        viewModel.NewPlaylistName = "Work";
        viewModel.CreatePlaylistCommand.Execute(null);
        viewModel.Playlists.Should().ContainSingle().Which.Name.Should().Be("Work");
        viewModel.IsPlaylistViewSelected.Should().BeTrue("a new playlist becomes the current view");
        viewModel.Tracks.Should().BeEmpty();

        viewModel.SelectedView = viewModel.Views[0];
        foreach (var title in new[] { "C", "A", "B" })
        {
            viewModel.SelectedTrack = viewModel.Tracks.Single(track => track.Title == title);
            viewModel.AddToPlaylistCommand.Execute(null);
        }

        viewModel.AddToPlaylistCommand.Execute(null);
        viewModel.Playlists[0].Count.Should().Be(3, "duplicates are ignored");

        viewModel.SelectedView = viewModel.Views.Single(static view => view.Kind == LibraryViewKind.Playlist);
        viewModel.Tracks.Select(static track => track.Title).Should().Equal(new[] { "C", "A", "B" }, "playlists keep insertion order regardless of the sort selector");

        viewModel.SelectedTrack = viewModel.Tracks[2];
        viewModel.MoveTrackUpCommand.Execute(null);
        viewModel.Tracks.Select(static track => track.Title).Should().Equal("C", "B", "A");

        viewModel.RemoveFromPlaylistCommand.Execute(viewModel.Tracks[0]);
        viewModel.Tracks.Select(static track => track.Title).Should().Equal("B", "A");

        viewModel.NewPlaylistName = "Renamed";
        viewModel.RenamePlaylistCommand.Execute(null);
        viewModel.SelectedView!.Name.Should().Be("Renamed");

        await viewModel.PendingSave;
        environment.Settings.Current.Playlists.Should().ContainSingle().Which.Name.Should().Be("Renamed");

        viewModel.DeletePlaylistCommand.Execute(null);
        viewModel.Playlists.Should().BeEmpty();
        viewModel.SelectedView!.Kind.Should().Be(LibraryViewKind.All);
    }

    [Fact]
    public async Task Settings_RestoreOnStartupIncludingPlaylistsEqualizerAndLastTrack()
    {
        using var viewModel = createViewModel(out var audio, out var environment);
        var set = createSet("Restored", "Artist", "Mapper", "", 150, 100);
        environment.Loader.Sets = [set, createSet("Other", "Artist", "Mapper", "", 150, 100)];
        environment.Locator.Detected = [new OsuInstallation(OsuInstallationKind.Lazer, Path.GetTempPath())];
        environment.Settings.Current = new AppSettings
        {
            Volume = 0.3,
            Mod = OsuAudioMod.NC,
            Shuffle = true,
            Repeat = RepeatMode.All,
            HitsoundsEnabled = true,
            HitsoundOffsetMs = 25,
            EqualizerGains = Equalizer.Presets["Rock"],
            Favourites = [set.Id],
            Playlists = [new PlaylistSetting(Guid.NewGuid(), "Saved", [set.Id])],
            LastPlayback = new PlaybackStateSetting(set.Id, 42, [set.Id]),
        };

        await viewModel.InitializeAsync();

        viewModel.Volume.Should().Be(0.3);
        audio.Mod.Should().Be(OsuAudioMod.NC);
        viewModel.IsShuffleEnabled.Should().BeTrue();
        viewModel.RepeatMode.Should().Be(RepeatMode.All);
        environment.Hitsounds.IsEnabled.Should().BeTrue();
        environment.Hitsounds.OffsetMs.Should().Be(25);
        viewModel.SelectedEqualizerPreset.Should().Be("Rock");
        audio.EqualizerGains.Should().Equal(Equalizer.Presets["Rock"]);
        viewModel.Playlists.Should().ContainSingle().Which.Count.Should().Be(1);
        viewModel.Views.Single(static view => view.Kind == LibraryViewKind.Favourites).Count.Should().Be(1);
        viewModel.CurrentTrack!.Title.Should().Be("Restored");
        viewModel.IsPlaying.Should().BeFalse("the last track is restored paused");
        audio.LastSeek.Should().Be(TimeSpan.FromSeconds(42));
        viewModel.Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task Equalizer_PresetAppliesGainsAndManualChangeMakesItCustom()
    {
        using var viewModel = createViewModel(out var audio);

        viewModel.SelectedEqualizerPreset = "Bass Boost";
        audio.EqualizerGains.Should().Equal(Equalizer.Presets["Bass Boost"]);
        viewModel.EqualizerPresetText.Should().Be("Bass Boost");

        viewModel.EqualizerBands[9].Gain = 3;
        audio.EqualizerGains[9].Should().Be(3f);
        viewModel.EqualizerPresetText.Should().Be("Custom");

        viewModel.ResetEqualizerCommand.Execute(null);
        audio.EqualizerGains.Should().AllBeEquivalentTo(0f);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Collections_BecomeViewsResolvedByBeatmapHash()
    {
        using var viewModel = createViewModel(out _, out var environment);
        var set = createSet("In collection", "Artist", "Mapper", "", 150, 100) with
        {
            Beatmaps = [new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "Normal", Tags = "", Md5Hash = "abc" }],
        };
        environment.Loader.Sets = [set, createSet("Other", "Artist", "Mapper", "", 150, 100)];
        environment.Locator.Detected = [new OsuInstallation(OsuInstallationKind.Lazer, Path.GetTempPath())];
        environment.Collections.Collections = [new BeatmapCollectionInfo("Practice", OsuInstallationKind.Lazer, ["ABC", "missing"])];

        await viewModel.InitializeAsync();

        var view = viewModel.Views.Single(static view => view.Kind == LibraryViewKind.Collection);
        view.Name.Should().Be("Practice");
        view.Count.Should().Be(1);
        viewModel.SelectedView = view;
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("In collection");
    }

    [Fact]
    public void Search_SupportsFieldSyntaxAndBrowseChips()
    {
        using var viewModel = createViewModel(out _);
        viewModel.ReplaceTracksForTesting([
            createSet("Song A", "Camellia", "Mapper One", "electronic speedcore", 200, 100),
            createSet("Song B", "YOASOBI", "Mapper Two", "anime jpop", 166, 100),
            createSet("Song C", "Camellia", "Mapper Two", "electronic", 180, 100),
        ]);

        viewModel.SearchText = "artist:camellia bpm:>190";
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("Song A");

        viewModel.TopArtists[0].Label.Should().Be("Camellia");
        viewModel.TopArtists[0].Count.Should().Be(2);
        viewModel.SelectBrowseCommand.Execute(viewModel.TopTags.Single(static tag => tag.Label == "electronic"));
        viewModel.SearchText.Should().Be("tag:\"electronic\"");
        viewModel.Tracks.Should().HaveCount(2);
    }

    [Fact]
    public void OpenOnWeb_UsesTheOnlineSetId()
    {
        using var viewModel = createViewModel(out _, out var environment);
        viewModel.ReplaceTracksForTesting([createSet("Online", "Artist", "Mapper", "", 100, 100) with { OnlineId = 1234 }, createSet("Local", "Artist", "Mapper", "", 100, 100)]);

        viewModel.SelectedTrack = viewModel.Tracks.Single(static track => track.Title == "Online");
        viewModel.CanOpenOnWeb.Should().BeTrue();
        viewModel.OpenOnWebCommand.Execute(null);
        environment.Links.Opened.Should().ContainSingle().Which.ToString().Should().Be("https://osu.ppy.sh/beatmapsets/1234");

        viewModel.SelectedTrack = viewModel.Tracks.Single(static track => track.Title == "Local");
        viewModel.CanOpenOnWeb.Should().BeFalse();
    }

    [Fact]
    public void SmartPlaylists_SaveTheSearchAndStayLive()
    {
        using var viewModel = createViewModel(out _);
        viewModel.ReplaceTracksForTesting([
            createSet("Fast", "Artist", "Mapper", "electronic", 220, 100),
            createSet("Slow", "Artist", "Mapper", "electronic", 120, 100),
        ]);

        viewModel.SearchText = "tag:electronic bpm:>200";
        viewModel.NewPlaylistName = "Speed";
        viewModel.CreateSmartPlaylistCommand.Execute(null);

        viewModel.SearchText.Should().BeEmpty("the query now lives in the smart playlist");
        viewModel.IsSmartPlaylistViewSelected.Should().BeTrue();
        viewModel.SelectedView!.Name.Should().Be("Speed");
        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("Fast");
        viewModel.BuildSettings().SmartPlaylists.Should().ContainSingle().Which.Query.Should().Be("tag:electronic bpm:>200");

        viewModel.DeleteSmartPlaylistCommand.Execute(null);
        viewModel.SmartPlaylists.Should().BeEmpty();
        viewModel.SelectedView!.Kind.Should().Be(LibraryViewKind.All);
    }

    [Fact]
    public async Task ExportView_WritesCollectionDbOutsideOsuFolders()
    {
        using var directory = new TestDirectory();
        var target = Path.Combine(directory.Path, "out", "collection.db");
        var saver = new FakeFileSaver { NextPath = target };
        using var viewModel = createViewModel(out _, out var environment, saver);
        viewModel.ReplaceTracksForTesting([createSet("A", "Artist", "Mapper", "", 100, 100) with
        {
            Beatmaps = [new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "N", Tags = "", Md5Hash = "abc" }],
        }]);

        await viewModel.ExportViewAsCollectionCommand.ExecuteAsync(null);

        File.Exists(target).Should().BeTrue();
        viewModel.LibraryStatusText.Should().Contain("1 譜面");
        environment.Links.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task Exclusions_HideShortLongAndQueryMatchedSetsAndPersist()
    {
        using var viewModel = createViewModel(out _, out var environment);
        environment.Locator.Detected = [new OsuInstallation(OsuInstallationKind.Lazer, Path.GetTempPath())];
        environment.Loader.Sets =
        [
            createSet("Short", "Artist", "Mapper", "", 100, 20),
            createSet("Normal", "Artist", "Mapper", "", 100, 200),
            createSet("Long", "Artist", "Mapper", "", 100, 900),
            createSet("Mania", "Artist", "Mapper", "", 100, 200) with { Beatmaps = [new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "4K", Tags = "", Ruleset = OsuRuleset.Mania, Length = TimeSpan.FromSeconds(200) }] },
        ];
        environment.Settings.Current = new AppSettings { Exclusions = new LibraryExclusionSettings { MinimumLengthSeconds = 30 } };

        await viewModel.InitializeAsync();

        viewModel.Tracks.Select(static track => track.Title).Should().BeEquivalentTo("Normal", "Long", "Mania");
        viewModel.LibraryStatusText.Should().Contain("1 excluded");

        viewModel.ExcludeMaxLengthSeconds = 600;
        viewModel.ExcludeQueryText = "mode:mania";
        viewModel.ApplyExclusionsCommand.Execute(null);

        viewModel.Tracks.Should().ContainSingle().Which.Title.Should().Be("Normal");
        viewModel.ExclusionStatusText.Should().Contain("3");
        var saved = viewModel.BuildSettings().Exclusions;
        saved.MinimumLengthSeconds.Should().Be(30);
        saved.MaximumLengthSeconds.Should().Be(600);
        saved.ExcludeQuery.Should().Be("mode:mania");

        viewModel.ExcludeMinLengthSeconds = 0;
        viewModel.ExcludeMaxLengthSeconds = 0;
        viewModel.ExcludeQueryText = string.Empty;
        viewModel.ApplyExclusionsCommand.Execute(null);
        viewModel.Tracks.Should().HaveCount(4);
    }

    [Fact]
    public async Task ZeroLengthSets_GetTheirDurationFromTheAudioFile()
    {
        var probe = new FakeDurationProbe();
        using var viewModel = createViewModel(out _, out var environment, durationProbe: probe);
        environment.Locator.Detected = [new OsuInstallation(OsuInstallationKind.Lazer, Path.GetTempPath())];
        environment.Loader.Sets = [createSet("WIP", "Artist", "Mapper", "", 100, 0), createSet("Done", "Artist", "Mapper", "", 100, 120)];

        await viewModel.InitializeAsync();

        probe.Probed.Should().Equal(new[] { "WIP.mp3" }, "only sets without a known length are probed");
        viewModel.Tracks.Single(static track => track.Title == "WIP").LengthText.Should().Be("1:30");
        viewModel.Tracks.Single(static track => track.Title == "Done").LengthText.Should().Be("2:00");
    }

    private sealed class FakeDurationProbe : IAudioDurationProbe
    {
        public List<string> Probed { get; } = [];
        public TimeSpan? Probe(string audioFilePath) { Probed.Add(audioFilePath); return TimeSpan.FromSeconds(90); }
    }

    private sealed class FakeFileSaver : IFileSaver
    {
        public string? NextPath { get; set; }
        public Task<string?> PickSavePathAsync(string title, string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult(NextPath);
    }

    private static string createLazerInstallation(TestDirectory directory, string name)
    {
        directory.CreateFile(Path.Combine(name, "client.realm"));
        return directory.CreateDirectory(name, "files") is { } _ ? Path.GetFullPath(Path.Combine(directory.Path, name)) : throw new InvalidOperationException();
    }

    private static MainWindowViewModel createViewModel(out FakeAudioEngine audio) => createViewModel(out audio, out _);

    private static MainWindowViewModel createViewModel(out FakeAudioEngine audio, out TestEnvironment environment, IFileSaver? fileSaver = null, IAudioDurationProbe? durationProbe = null)
    {
        audio = new FakeAudioEngine();
        environment = new TestEnvironment();
        var manager = new BeatmapManager([environment.Loader], new DuplicateDetector());
        return new MainWindowViewModel(manager, audio, environment.Locator, new ImmediateDispatcher(), new NullImageLoader(), environment.Settings, environment.Picker, environment.Media, environment.Video, environment.Hitsounds, new HitsoundSampleSourceFactory(static () => null), environment.Storyboard, environment.Links, [environment.Collections], null, fileSaver, durationProbe);
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
        public FakeHitsoundPlayer Hitsounds { get; } = new();
        public FakeStoryboardLoader Storyboard { get; } = new();
        public FakeLinkOpener Links { get; } = new();
        public FakeCollectionLoader Collections { get; } = new();
    }

    private sealed class FakeLinkOpener : ILinkOpener
    {
        public List<Uri> Opened { get; } = [];
        public bool Open(Uri uri) { Opened.Add(uri); return true; }
    }

    private sealed class FakeCollectionLoader : ICollectionLoader
    {
        public OsuInstallationKind Kind => OsuInstallationKind.Lazer;
        public IReadOnlyList<BeatmapCollectionInfo> Collections { get; set; } = [];
        public Task<IReadOnlyList<BeatmapCollectionInfo>> LoadAsync(string installationPath, CancellationToken cancellationToken = default) => Task.FromResult(Collections);
    }

    private sealed class FakeHitsoundPlayer : IHitsoundPlayer
    {
        public event EventHandler<HitsoundEvent>? HitPlayed { add { } remove { } }
        public bool IsEnabled { get; set; }
        public float Volume { get; set; } = 1;
        public int OffsetMs { get; set; }
        public int MissingSampleCount => 0;
        public List<int> Loads { get; } = [];
        public int Clears { get; private set; }

        public Task LoadAsync(IReadOnlyList<HitsoundEvent> events, HitsoundSampleResolver resolver, CancellationToken cancellationToken = default)
        {
            Loads.Add(events.Count);
            return Task.CompletedTask;
        }

        public void Clear() => Clears++;
        public void Dispose() { }
    }

    private sealed class FakeStoryboardLoader : IStoryboardLoader
    {
        public Func<StoryboardSession?> Next { get; set; } = static () => null;
        public int Loads { get; private set; }
        public string? LastBeatmapPath { get; private set; }

        public Task<StoryboardSession?> LoadAsync(string? storyboardFilePath, string? beatmapFilePath, IBeatmapFileResolver files, CancellationToken cancellationToken = default)
        {
            Loads++;
            LastBeatmapPath = beatmapFilePath;
            return Task.FromResult(Next());
        }
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
        public IReadOnlyList<float> EqualizerGains { get; private set; } = new float[Equalizer.BandCount];
        public void SetEqualizer(IReadOnlyList<float> gainsDb) => EqualizerGains = Equalizer.Normalize(gainsDb);
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
