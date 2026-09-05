using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly BeatmapManager beatmapManager;
    private readonly IAudioEngine audioEngine;
    private readonly IOsuInstallationLocator installationLocator;
    private readonly IUiDispatcher dispatcher;
    private readonly IBackgroundImageLoader imageLoader;
    private readonly ISettingsStore settingsStore;
    private readonly IFolderPicker folderPicker;
    private readonly IBeatmapMediaResolver mediaResolver;
    private readonly IVideoPlayer videoPlayer;
    private readonly IHitsoundPlayer hitsoundPlayer;
    private readonly IHitsoundSampleSourceFactory sampleSourceFactory;
    private readonly IStoryboardLoader storyboardLoader;
    private TrackItemViewModel? observedTrack;
    private DateTime lastVideoResync = DateTime.MinValue;
    private readonly List<TrackItemViewModel> allTracks = [];
    private readonly List<OsuInstallation> manualInstallations = [];
    private readonly List<TrackItemViewModel> shuffleHistory = [];
    private readonly Random random = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim libraryGate = new(1, 1);
    private CancellationTokenSource? libraryLoadCancellation;
    private bool updatingPosition;
    private bool initialized;
    private bool disposed;
    private long loadVersion;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private TrackSortOption selectedSort = TrackSortOption.Title;

    [ObservableProperty]
    private TrackItemViewModel? selectedTrack;

    [ObservableProperty]
    private TrackItemViewModel? currentTrack;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string libraryStatusText = string.Empty;

    [ObservableProperty]
    private bool isSourcesPanelVisible;

    [ObservableProperty]
    private bool isQueuePanelVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    [NotifyPropertyChangedFor(nameof(HasStoryboard))]
    [NotifyPropertyChangedFor(nameof(IsVideoVisible))]
    [NotifyPropertyChangedFor(nameof(VideoPanelHeight))]
    private BeatmapMedia currentMedia = BeatmapMedia.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoVisible))]
    [NotifyPropertyChangedFor(nameof(VideoPanelHeight))]
    private bool isVideoEnabled = true;

    [ObservableProperty]
    private bool isShuffleEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatText))]
    private RepeatMode repeatMode = RepeatMode.Off;

    [ObservableProperty]
    private bool isHitsoundEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStoryboardVisible))]
    [NotifyPropertyChangedFor(nameof(StoryboardPanelHeight))]
    private bool isStoryboardEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStoryboardVisible))]
    [NotifyPropertyChangedFor(nameof(StoryboardPanelHeight))]
    private StoryboardSession? storyboardSession;

    [ObservableProperty]
    private string visualsStatusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseText))]
    private bool isPlaying;

    [ObservableProperty]
    private TimeSpan currentTime;

    [ObservableProperty]
    private TimeSpan totalTime;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private double volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDtActive))]
    [NotifyPropertyChangedFor(nameof(IsNcActive))]
    [NotifyPropertyChangedFor(nameof(IsHtActive))]
    [NotifyPropertyChangedFor(nameof(IsDcActive))]
    private OsuAudioMod mod;

    public MainWindowViewModel(
        BeatmapManager beatmapManager,
        IAudioEngine audioEngine,
        IOsuInstallationLocator installationLocator,
        IUiDispatcher dispatcher,
        IBackgroundImageLoader imageLoader,
        ISettingsStore settingsStore,
        IFolderPicker folderPicker,
        IBeatmapMediaResolver mediaResolver,
        IVideoPlayer videoPlayer,
        IHitsoundPlayer hitsoundPlayer,
        IHitsoundSampleSourceFactory sampleSourceFactory,
        IStoryboardLoader storyboardLoader)
    {
        this.beatmapManager = beatmapManager ?? throw new ArgumentNullException(nameof(beatmapManager));
        this.audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        this.installationLocator = installationLocator ?? throw new ArgumentNullException(nameof(installationLocator));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        this.mediaResolver = mediaResolver ?? throw new ArgumentNullException(nameof(mediaResolver));
        this.videoPlayer = videoPlayer ?? throw new ArgumentNullException(nameof(videoPlayer));
        this.hitsoundPlayer = hitsoundPlayer ?? throw new ArgumentNullException(nameof(hitsoundPlayer));
        this.sampleSourceFactory = sampleSourceFactory ?? throw new ArgumentNullException(nameof(sampleSourceFactory));
        this.storyboardLoader = storyboardLoader ?? throw new ArgumentNullException(nameof(storyboardLoader));
        volume = audioEngine.Volume;
        mod = audioEngine.Mod;
        audioEngine.PositionChanged += onPositionChanged;
        audioEngine.PlaybackEnded += onPlaybackEnded;
    }

    public ObservableCollection<TrackItemViewModel> Tracks { get; } = [];
    public ObservableCollection<InstallationItemViewModel> Installations { get; } = [];
    public ObservableCollection<TrackItemViewModel> Queue { get; } = [];
    public bool HasQueue => Queue.Count > 0;
    public TrackItemViewModel? DetailTrack => SelectedTrack ?? CurrentTrack;
    public bool HasDetailTrack => DetailTrack is not null;
    public bool HasVideo => CurrentMedia.HasVideo;
    public bool HasStoryboard => CurrentMedia.HasStoryboard;
    public bool IsVideoAvailable => videoPlayer.IsAvailable;
    public string VideoUnavailableText => videoPlayer.UnavailableReason ?? string.Empty;
    public bool IsVideoVisible => IsVideoEnabled && HasVideo && videoPlayer.IsAvailable;
    public bool IsStoryboardVisible => IsStoryboardEnabled && StoryboardSession is not null;

    /// <summary>Like the video surface, the storyboard box collapses to zero height instead of hiding.</summary>
    public double StoryboardPanelHeight => IsStoryboardVisible ? 202 : 0;

    /// <summary>The most recent hit sound / storyboard load, awaited by tests.</summary>
    internal Task LastVisualsLoad { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The video surface must stay attached to the window even while hidden, otherwise
    /// libVLC has no window handle when playback starts and opens its own window. The
    /// view therefore collapses the surface to zero height instead of hiding it.
    /// </summary>
    public double VideoPanelHeight => IsVideoVisible ? 202 : 0;
    public string QueueText => Queue.Count == 0 ? "Queue" : $"Queue ({Queue.Count})";
    public string RepeatText => RepeatMode switch
    {
        RepeatMode.All => "Repeat: All",
        RepeatMode.One => "Repeat: One",
        _ => "Repeat: Off",
    };
    public IReadOnlyList<TrackSortOption> SortOptions { get; } = Enum.GetValues<TrackSortOption>();
    public string PlayPauseText => IsPlaying ? "Pause" : "Play";
    public bool HasTracks => Tracks.Count > 0;
    public bool HasInstallations => Installations.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanAddStableFolder => OperatingSystem.IsWindows();
    public string EmptyStateText => HasInstallations
        ? "No playable beatmaps found"
        : "No osu! installation was detected. Open Sources and add your osu! data folder.";
    public string CurrentTimeText => formatTime(CurrentTime);
    public string TotalTimeText => formatTime(TotalTime);
    public bool IsDtActive => Mod == OsuAudioMod.DT;
    public bool IsNcActive => Mod == OsuAudioMod.NC;
    public bool IsHtActive => Mod == OsuAudioMod.HT;
    public bool IsDcActive => Mod == OsuAudioMod.DC;

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (initialized || disposed)
        {
            return;
        }

        initialized = true;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var settings = await settingsStore.LoadAsync(lifetimeCancellation.Token).ConfigureAwait(false);
            manualInstallations.Clear();
            foreach (var setting in settings.ManualInstallations)
            {
                var installation = installationLocator.ValidateManualPath(setting.Path, setting.Kind);
                if (installation is not null && !manualInstallations.Any(existing => sameInstallation(existing, installation)))
                {
                    manualInstallations.Add(installation);
                }
            }
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await dispatcher.InvokeAsync(() => ErrorMessage = $"設定を読み込めませんでした: {exception.Message}").ConfigureAwait(false);
        }

        await reloadLibraryAsync().ConfigureAwait(false);
        await dispatcher.InvokeAsync(() =>
        {
            if (!HasInstallations)
            {
                IsSourcesPanelVisible = true;
            }
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    private Task PlaySelectedAsync() => playTrackAsync(SelectedTrack ?? CurrentTrack);

    /// <summary>Plays the selected track from its song-select preview point.</summary>
    [RelayCommand]
    private Task PreviewSelectedAsync()
    {
        var track = SelectedTrack ?? CurrentTrack;
        return playTrackAsync(track, track?.PreviewTime);
    }

    [RelayCommand]
    private void TogglePlay()
    {
        try
        {
            if (CurrentTrack is null)
            {
                _ = playTrackAsync(SelectedTrack ?? Tracks.FirstOrDefault());
                return;
            }

            audioEngine.TogglePlay();
            IsPlaying = audioEngine.State == AudioPlaybackState.Playing;
            syncVideoPlayState();
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            reportPlaybackError(exception);
        }
    }

    partial void OnIsHitsoundEnabledChanged(bool value)
    {
        hitsoundPlayer.IsEnabled = value;
        if (value && CurrentTrack is { } track)
        {
            LastVisualsLoad = loadHitsoundsAsync(track, Volatile.Read(ref loadVersion));
        }
    }

    partial void OnIsStoryboardEnabledChanged(bool value)
    {
        if (value && CurrentTrack is { } track && StoryboardSession is null && CurrentMedia.HasStoryboard)
        {
            LastVisualsLoad = loadStoryboardAsync(track, Volatile.Read(ref loadVersion));
        }
    }

    partial void OnIsVideoEnabledChanged(bool value)
    {
        if (value)
        {
            startVideoIfAvailable();
        }
        else
        {
            videoPlayer.Stop();
        }
    }

    private static double modToRate(OsuAudioMod audioMod) => audioMod switch
    {
        OsuAudioMod.DT or OsuAudioMod.NC => 1.5,
        OsuAudioMod.HT or OsuAudioMod.DC => 0.75,
        _ => 1,
    };

    private TimeSpan toVideoTime(TimeSpan audioTime) => audioTime - CurrentMedia.VideoOffset;

    /// <summary>Loads the current track's video (if any) and aligns it with the audio clock.</summary>
    private void startVideoIfAvailable()
    {
        if (!videoPlayer.IsAvailable || !IsVideoEnabled || CurrentMedia.VideoFilePath is not { } videoPath)
        {
            return;
        }

        try
        {
            var start = toVideoTime(audioEngine.CurrentTime);
            videoPlayer.Load(videoPath, start < TimeSpan.Zero ? TimeSpan.Zero : start);
            videoPlayer.SetRate(modToRate(Mod));
            syncVideoPlayState();
            lastVideoResync = DateTime.UtcNow;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ErrorMessage = $"動画を再生できませんでした: {exception.Message}";
        }
    }

    private void syncVideoPlayState()
    {
        if (videoPlayer.CurrentPath is null)
        {
            return;
        }

        if (audioEngine.State == AudioPlaybackState.Playing)
        {
            videoPlayer.Play();
        }
        else
        {
            videoPlayer.Pause();
        }
    }

    /// <summary>Nudges the video back onto the audio clock when it drifts, at most once per second.</summary>
    private void resyncVideo(TimeSpan audioTime)
    {
        if (videoPlayer.CurrentPath is null || !IsPlaying)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastVideoResync < TimeSpan.FromSeconds(1))
        {
            return;
        }

        var expected = toVideoTime(audioTime);
        if (expected < TimeSpan.Zero)
        {
            return;
        }

        var drift = videoPlayer.Position - expected;
        if (drift > TimeSpan.FromMilliseconds(250) || drift < TimeSpan.FromMilliseconds(-250))
        {
            videoPlayer.Seek(expected);
            lastVideoResync = now;
        }
    }

    [RelayCommand]
    private Task NextAsync() => moveAsync(1);

    [RelayCommand]
    private Task PreviousAsync() => moveAsync(-1);

    [RelayCommand]
    private void SetMod(OsuAudioMod selectedMod)
    {
        try
        {
            Mod = Mod == selectedMod ? OsuAudioMod.None : selectedMod;
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            reportPlaybackError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddStableFolder))]
    private Task AddStableFolderAsync() => addFolderAsync(OsuInstallationKind.Stable);

    [RelayCommand]
    private Task AddLazerFolderAsync() => addFolderAsync(OsuInstallationKind.Lazer);

    [RelayCommand]
    private Task ReloadLibraryAsync() => reloadLibraryAsync();

    [RelayCommand]
    private void CycleRepeat() => RepeatMode = RepeatMode switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.Off,
    };

    [RelayCommand]
    private void EnqueueSelected()
    {
        if (SelectedTrack is { } track && allTracks.Contains(track))
        {
            Queue.Add(track);
            notifyQueueChanged();
        }
    }

    [RelayCommand]
    private void RemoveFromQueue(TrackItemViewModel? track)
    {
        if (track is not null && Queue.Remove(track))
        {
            notifyQueueChanged();
        }
    }

    [RelayCommand]
    private void ClearQueue()
    {
        if (Queue.Count == 0)
        {
            return;
        }

        Queue.Clear();
        notifyQueueChanged();
    }

    partial void OnIsShuffleEnabledChanged(bool value) => shuffleHistory.Clear();

    partial void OnSearchTextChanged(string value) => applyFilterAndSort();

    partial void OnSelectedTrackChanged(TrackItemViewModel? value) => notifyDetailTrackChanged();

    partial void OnCurrentTrackChanged(TrackItemViewModel? value)
    {
        if (observedTrack is not null)
        {
            observedTrack.PropertyChanged -= onCurrentTrackPropertyChanged;
        }

        observedTrack = value;
        if (observedTrack is not null)
        {
            observedTrack.PropertyChanged += onCurrentTrackPropertyChanged;
        }

        notifyDetailTrackChanged();
    }

    private void onCurrentTrackPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        // Switching the difficulty swaps the hit sounds and the .osu storyboard events.
        if (args.PropertyName == nameof(TrackItemViewModel.SelectedDifficulty) && sender is TrackItemViewModel track && track == CurrentTrack)
        {
            LastVisualsLoad = loadVisualsAsync(track, Volatile.Read(ref loadVersion));
        }
    }

    private void notifyDetailTrackChanged()
    {
        OnPropertyChanged(nameof(DetailTrack));
        OnPropertyChanged(nameof(HasDetailTrack));
    }
    partial void OnSelectedSortChanged(TrackSortOption value) => applyFilterAndSort();

    partial void OnProgressChanged(double value)
    {
        if (updatingPosition || TotalTime <= TimeSpan.Zero)
        {
            return;
        }

        seek(value);
    }

    [RelayCommand]
    private void Seek(double requestedProgress) => seek(requestedProgress);

    partial void OnVolumeChanged(double value)
    {
        var clamped = Math.Clamp(value, 0, 1);
        if (clamped != value)
        {
            Volume = clamped;
            return;
        }

        audioEngine.Volume = (float)clamped;
    }

    partial void OnModChanged(OsuAudioMod value)
    {
        audioEngine.Mod = value;
        if (videoPlayer.CurrentPath is not null)
        {
            videoPlayer.SetRate(modToRate(value));
        }
    }

    partial void OnCurrentTimeChanged(TimeSpan value) => OnPropertyChanged(nameof(CurrentTimeText));
    partial void OnTotalTimeChanged(TimeSpan value) => OnPropertyChanged(nameof(TotalTimeText));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        libraryLoadCancellation?.Cancel();
        audioEngine.PositionChanged -= onPositionChanged;
        audioEngine.PlaybackEnded -= onPlaybackEnded;
        videoPlayer.Stop();
        hitsoundPlayer.Clear();
        clearStoryboard();
        if (observedTrack is not null)
        {
            observedTrack.PropertyChanged -= onCurrentTrackPropertyChanged;
            observedTrack = null;
        }
        foreach (var track in allTracks)
        {
            track.Dispose();
        }

        libraryLoadCancellation?.Dispose();
        lifetimeCancellation.Dispose();
        libraryGate.Dispose();
    }

    internal void ReplaceTracksForTesting(IEnumerable<UnifiedBeatmapSet> models) =>
        replaceTracks(models.Select(model => new TrackItemViewModel(model, imageLoader)).ToArray());

    private async Task addFolderAsync(OsuInstallationKind kind)
    {
        if (disposed)
        {
            return;
        }

        var kindName = kind == OsuInstallationKind.Stable ? "osu!stable" : "osu!lazer";
        string? path;
        try
        {
            path = await folderPicker.PickFolderAsync($"Select the {kindName} data folder", lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var installation = installationLocator.ValidateManualPath(path, kind);
        if (installation is null)
        {
            var expected = kind == OsuInstallationKind.Stable ? "osu!.db と Songs フォルダー" : "client.realm と files フォルダー";
            await dispatcher.InvokeAsync(() => ErrorMessage = $"{kindName} のデータフォルダーではありません。{expected} を含むフォルダーを選択してください: {path}").ConfigureAwait(false);
            return;
        }

        if (Installations.Any(item => item.Matches(installation)))
        {
            await dispatcher.InvokeAsync(() =>
            {
                ErrorMessage = null;
                LibraryStatusText = $"既に追加されています: {installation.RootPath}";
            }).ConfigureAwait(false);
            return;
        }

        manualInstallations.Add(installation);
        await dispatcher.InvokeAsync(() => ErrorMessage = null).ConfigureAwait(false);
        await saveSettingsAsync().ConfigureAwait(false);
        await reloadLibraryAsync().ConfigureAwait(false);
    }

    private async Task removeInstallationAsync(InstallationItemViewModel item)
    {
        if (disposed || !item.IsManual)
        {
            return;
        }

        if (manualInstallations.RemoveAll(existing => item.Matches(existing)) == 0)
        {
            return;
        }

        await saveSettingsAsync().ConfigureAwait(false);
        await reloadLibraryAsync().ConfigureAwait(false);
    }

    private async Task saveSettingsAsync()
    {
        var settings = new AppSettings
        {
            ManualInstallations = manualInstallations
                .Select(static installation => new ManualInstallationSetting(installation.Kind, installation.RootPath))
                .ToArray(),
        };

        try
        {
            await settingsStore.SaveAsync(settings, lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await dispatcher.InvokeAsync(() => ErrorMessage = $"設定を保存できませんでした: {exception.Message}").ConfigureAwait(false);
        }
    }

    private async Task reloadLibraryAsync()
    {
        if (disposed)
        {
            return;
        }

        // A newer reload always wins: cancel whatever scan is still running.
        var previous = Interlocked.Exchange(ref libraryLoadCancellation, CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token));
        previous?.Cancel();
        var cancellation = libraryLoadCancellation!;
        var token = cancellation.Token;

        await libraryGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            previous?.Dispose();
            if (token.IsCancellationRequested)
            {
                return;
            }

            var installations = collectInstallations();
            await dispatcher.InvokeAsync(() =>
            {
                replaceInstallations(installations);
                IsLoading = true;
                ErrorMessage = null;
                LibraryStatusText = installations.Count == 0 ? string.Empty : "Loading library…";
            }).ConfigureAwait(false);

            var models = await beatmapManager.LoadAsync(installations.Select(static entry => entry.Installation), token).ConfigureAwait(false);
            var items = models
                .Where(static model => !string.IsNullOrWhiteSpace(model.AudioFilePath))
                .Select(model => new TrackItemViewModel(model, imageLoader))
                .ToArray();
            token.ThrowIfCancellationRequested();
            await dispatcher.InvokeAsync(() =>
            {
                replaceTracks(items);
                LibraryStatusText = installations.Count == 0
                    ? string.Empty
                    : $"{items.Length:N0} tracks from {installations.Count} source{(installations.Count == 1 ? string.Empty : "s")}";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await dispatcher.InvokeAsync(() => ErrorMessage = $"ライブラリを読み込めませんでした: {exception.Message}").ConfigureAwait(false);
        }
        finally
        {
            if (!token.IsCancellationRequested || lifetimeCancellation.IsCancellationRequested)
            {
                await dispatcher.InvokeAsync(() => IsLoading = false).ConfigureAwait(false);
            }

            libraryGate.Release();
        }
    }

    private List<(OsuInstallation Installation, bool IsManual)> collectInstallations()
    {
        var result = new List<(OsuInstallation Installation, bool IsManual)>();
        foreach (var detected in installationLocator.FindInstallations())
        {
            if (!result.Any(entry => sameInstallation(entry.Installation, detected)))
            {
                result.Add((detected, false));
            }
        }

        foreach (var manual in manualInstallations)
        {
            if (!result.Any(entry => sameInstallation(entry.Installation, manual)))
            {
                result.Add((manual, true));
            }
        }

        return result;
    }

    private void replaceInstallations(IEnumerable<(OsuInstallation Installation, bool IsManual)> installations)
    {
        Installations.Clear();
        foreach (var (installation, isManual) in installations)
        {
            Installations.Add(new InstallationItemViewModel(installation, isManual, removeInstallationAsync));
        }

        OnPropertyChanged(nameof(HasInstallations));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private void replaceTracks(IReadOnlyCollection<TrackItemViewModel> items)
    {
        var previous = allTracks.ToArray();
        var currentId = CurrentTrack?.Model.Id;
        var selectedId = SelectedTrack?.Model.Id;

        allTracks.Clear();
        allTracks.AddRange(items);
        applyFilterAndSort();

        // Rebind the player to the freshly created items before the old ones (and their
        // bitmaps) are disposed, so the UI never renders a disposed image.
        CurrentTrack = currentId is null ? null : allTracks.FirstOrDefault(track => track.Model.Id == currentId);
        SelectedTrack = selectedId is null ? null : allTracks.FirstOrDefault(track => track.Model.Id == selectedId);

        for (var i = Queue.Count - 1; i >= 0; i--)
        {
            var replacement = allTracks.FirstOrDefault(track => track.Model.Id == Queue[i].Model.Id);
            if (replacement is null)
            {
                Queue.RemoveAt(i);
            }
            else
            {
                Queue[i] = replacement;
            }
        }

        notifyQueueChanged();
        shuffleHistory.Clear();

        foreach (var track in previous)
        {
            track.Dispose();
        }
    }

    private void applyFilterAndSort()
    {
        IEnumerable<TrackItemViewModel> query = allTracks;
        var terms = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length > 0)
        {
            // Every word must match some field, so "zero centimeters mikan" narrows by title and mapper.
            query = query.Where(track => terms.All(term =>
                track.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                track.Artist.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                track.Model.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                track.Model.TitleUnicode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                track.Model.Artist.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                track.Model.ArtistUnicode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                track.Creator.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                track.Tags.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        query = SelectedSort switch
        {
            TrackSortOption.Artist => query.OrderBy(static track => track.Artist, StringComparer.CurrentCultureIgnoreCase),
            TrackSortOption.BPM => query.OrderByDescending(static track => track.BPM),
            TrackSortOption.Length => query.OrderByDescending(static track => track.Length),
            _ => query.OrderBy(static track => track.Title, StringComparer.CurrentCultureIgnoreCase),
        };

        var selected = SelectedTrack;
        Tracks.Clear();
        foreach (var track in query)
        {
            Tracks.Add(track);
        }

        OnPropertyChanged(nameof(HasTracks));

        if (selected is not null && Tracks.Contains(selected))
        {
            SelectedTrack = selected;
        }
    }

    private Task moveAsync(int offset) => offset > 0 ? advanceAsync(automatic: false) : previousAsync();

    /// <summary>
    /// Picks what plays next: the queue first, then shuffle or the sequential order.
    /// An automatic advance (track ended) stops at the end of the list unless repeat
    /// is set to All; a manual "Next" always wraps around.
    /// </summary>
    private async Task advanceAsync(bool automatic)
    {
        if (Queue.Count > 0)
        {
            var queued = Queue[0];
            Queue.RemoveAt(0);
            notifyQueueChanged();
            if (allTracks.Contains(queued))
            {
                rememberForShuffle(CurrentTrack);
                await playTrackAsync(queued).ConfigureAwait(false);
                return;
            }
        }

        var list = Tracks.Count > 0 ? Tracks : new ObservableCollection<TrackItemViewModel>(allTracks);
        if (list.Count == 0)
        {
            return;
        }

        var anchor = CurrentTrack ?? SelectedTrack;
        TrackItemViewModel next;
        if (IsShuffleEnabled)
        {
            next = pickShuffleTrack(list, anchor);
        }
        else
        {
            var currentIndex = anchor is null ? -1 : list.IndexOf(anchor);
            var nextIndex = currentIndex + 1;
            if (nextIndex >= list.Count)
            {
                if (automatic && RepeatMode != RepeatMode.All)
                {
                    IsPlaying = false;
                    return;
                }

                nextIndex = 0;
            }

            next = list[nextIndex];
        }

        rememberForShuffle(anchor);
        SelectedTrack = next;
        await playTrackAsync(next).ConfigureAwait(false);
    }

    private async Task previousAsync()
    {
        if (IsShuffleEnabled && shuffleHistory.Count > 0)
        {
            var last = shuffleHistory[^1];
            shuffleHistory.RemoveAt(shuffleHistory.Count - 1);
            if (allTracks.Contains(last))
            {
                SelectedTrack = last;
                await playTrackAsync(last).ConfigureAwait(false);
                return;
            }
        }

        var list = Tracks.Count > 0 ? Tracks : new ObservableCollection<TrackItemViewModel>(allTracks);
        if (list.Count == 0)
        {
            return;
        }

        var anchor = CurrentTrack ?? SelectedTrack;
        var currentIndex = anchor is null ? 0 : list.IndexOf(anchor);
        var previousIndex = (currentIndex - 1 + list.Count) % list.Count;
        SelectedTrack = list[previousIndex];
        await playTrackAsync(SelectedTrack).ConfigureAwait(false);
    }

    private TrackItemViewModel pickShuffleTrack(IList<TrackItemViewModel> list, TrackItemViewModel? current)
    {
        if (list.Count == 1)
        {
            return list[0];
        }

        // Prefer tracks that have not been played in this shuffle round.
        var candidates = list.Where(track => track != current && !shuffleHistory.Contains(track)).ToList();
        if (candidates.Count == 0)
        {
            shuffleHistory.Clear();
            candidates = list.Where(track => track != current).ToList();
        }

        return candidates[random.Next(candidates.Count)];
    }

    private void rememberForShuffle(TrackItemViewModel? track)
    {
        if (track is null)
        {
            return;
        }

        shuffleHistory.Add(track);
        if (shuffleHistory.Count > 200)
        {
            shuffleHistory.RemoveAt(0);
        }
    }

    private async Task playTrackAsync(TrackItemViewModel? track, TimeSpan? startAt = null)
    {
        if (track?.Model.AudioFilePath is not { Length: > 0 } audioPath)
        {
            return;
        }

        var version = Interlocked.Increment(ref loadVersion);
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            videoPlayer.Stop();
            hitsoundPlayer.Clear();
            clearStoryboard();
            VisualsStatusText = string.Empty;
            await audioEngine.LoadAsync(audioPath).ConfigureAwait(false);
            if (version != Volatile.Read(ref loadVersion) || disposed)
            {
                return;
            }

            if (startAt is { } start && start > TimeSpan.Zero)
            {
                var total = audioEngine.TotalTime;
                audioEngine.Seek(total > TimeSpan.Zero && start >= total ? TimeSpan.Zero : start);
            }

            audioEngine.Play();
            await dispatcher.InvokeAsync(() =>
            {
                CurrentTrack = track;
                SelectedTrack = track;
                IsPlaying = audioEngine.State == AudioPlaybackState.Playing;
                CurrentMedia = BeatmapMedia.None;
                updatePosition(audioEngine.CurrentTime, audioEngine.TotalTime);
            }).ConfigureAwait(false);
            _ = resolveMediaAsync(track, version);
        }
        catch (Exception exception) when (exception is AudioEngineException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await dispatcher.InvokeAsync(() => reportPlaybackError(exception)).ConfigureAwait(false);
        }
        finally
        {
            await dispatcher.InvokeAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    private async Task resolveMediaAsync(TrackItemViewModel track, long version)
    {
        BeatmapMedia media;
        try
        {
            media = await mediaResolver.ResolveAsync(track.Model, lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            media = BeatmapMedia.None;
        }

        if (version != Volatile.Read(ref loadVersion) || disposed)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            CurrentMedia = media;
            startVideoIfAvailable();
        }).ConfigureAwait(false);
        LastVisualsLoad = loadVisualsAsync(track, version);
    }

    private async Task loadVisualsAsync(TrackItemViewModel track, long version)
    {
        var hitsounds = IsHitsoundEnabled ? loadHitsoundsAsync(track, version) : Task.CompletedTask;
        var storyboard = IsStoryboardEnabled && CurrentMedia.HasStoryboard ? loadStoryboardAsync(track, version) : Task.CompletedTask;
        await Task.WhenAll(hitsounds, storyboard).ConfigureAwait(false);
    }

    private async Task loadHitsoundsAsync(TrackItemViewModel track, long version)
    {
        var beatmapPath = track.SelectedDifficulty?.Model.BeatmapFilePath;
        if (beatmapPath is null || !IsHitsoundEnabled || disposed)
        {
            return;
        }

        try
        {
            var token = lifetimeCancellation.Token;
            OsuInstallation[] installations = [];
            await dispatcher.InvokeAsync(() => installations = Installations.Select(static item => item.Installation).ToArray()).ConfigureAwait(false);
            var events = await Task.Run(() => HitsoundTimelineBuilder.Build(beatmapPath), token).ConfigureAwait(false);
            if (version != Volatile.Read(ref loadVersion) || disposed)
            {
                return;
            }

            var resolver = sampleSourceFactory.Create(track.Model, installations);
            await hitsoundPlayer.LoadAsync(events, resolver, token).ConfigureAwait(false);
            if (version != Volatile.Read(ref loadVersion) || disposed)
            {
                return;
            }

            var missing = hitsoundPlayer.MissingSampleCount;
            var status = missing > 0 ? $"{events.Count:N0} hit sounds ({missing} samples missing)" : $"{events.Count:N0} hit sounds";
            await dispatcher.InvokeAsync(() => VisualsStatusText = appendStatus(VisualsStatusText, status)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The beatmap parser throws many exception types on damaged files; hit sounds are optional.
            await dispatcher.InvokeAsync(() => VisualsStatusText = appendStatus(VisualsStatusText, $"ヒットサウンドを読み込めませんでした: {exception.Message}")).ConfigureAwait(false);
        }
    }

    private async Task loadStoryboardAsync(TrackItemViewModel track, long version)
    {
        if (track.Model.Files is not { } files || disposed)
        {
            return;
        }

        var media = CurrentMedia;
        var beatmapPath = track.SelectedDifficulty?.Model.BeatmapFilePath;
        StoryboardSession? session = null;
        try
        {
            session = await storyboardLoader.LoadAsync(media.StoryboardFilePath, beatmapPath, files, lifetimeCancellation.Token).ConfigureAwait(false);
            if (version != Volatile.Read(ref loadVersion) || disposed)
            {
                session?.Dispose();
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                var previous = StoryboardSession;
                StoryboardSession = session;
                previous?.Dispose();
                if (session is not null)
                {
                    VisualsStatusText = appendStatus(VisualsStatusText, $"storyboard: {session.ObjectCount:N0} sprites");
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            session?.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            session?.Dispose();
            await dispatcher.InvokeAsync(() => VisualsStatusText = appendStatus(VisualsStatusText, $"ストーリーボードを読み込めませんでした: {exception.Message}")).ConfigureAwait(false);
        }
    }

    private void clearStoryboard()
    {
        var previous = StoryboardSession;
        StoryboardSession = null;
        previous?.Dispose();
    }

    private static string appendStatus(string current, string addition) =>
        string.IsNullOrEmpty(current) ? addition : current + "  ·  " + addition;

    private void onPositionChanged(object? sender, PlaybackPositionChangedEventArgs args) =>
        _ = dispatcher.InvokeAsync(() => updatePosition(args.CurrentTime, args.TotalTime));

    private void onPlaybackEnded(object? sender, EventArgs args) =>
        _ = dispatcher.InvokeAsync(() => _ = onTrackEndedAsync());

    private async Task onTrackEndedAsync()
    {
        if (disposed)
        {
            return;
        }

        if (RepeatMode == RepeatMode.One && CurrentTrack is not null)
        {
            try
            {
                audioEngine.Seek(TimeSpan.Zero);
                audioEngine.Play();
                IsPlaying = audioEngine.State == AudioPlaybackState.Playing;
                if (videoPlayer.CurrentPath is not null)
                {
                    videoPlayer.Seek(toVideoTime(TimeSpan.Zero) < TimeSpan.Zero ? TimeSpan.Zero : toVideoTime(TimeSpan.Zero));
                    syncVideoPlayState();
                }
            }
            catch (Exception exception)
            {
                reportPlaybackError(exception);
            }

            return;
        }

        await advanceAsync(automatic: true).ConfigureAwait(false);
    }

    private void notifyQueueChanged()
    {
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(QueueText));
    }

    private void updatePosition(TimeSpan current, TimeSpan total)
    {
        updatingPosition = true;
        try
        {
            CurrentTime = current;
            TotalTime = total;
            Progress = total > TimeSpan.Zero ? Math.Clamp(current.TotalSeconds / total.TotalSeconds, 0, 1) : 0;
            IsPlaying = audioEngine.State == AudioPlaybackState.Playing;
            resyncVideo(current);
        }
        finally
        {
            updatingPosition = false;
        }
    }

    private void reportPlaybackError(Exception exception)
    {
        IsPlaying = false;
        ErrorMessage = $"再生できませんでした: {exception.Message}";
    }

    private void seek(double requestedProgress)
    {
        try
        {
            var target = TimeSpan.FromTicks((long)(TotalTime.Ticks * Math.Clamp(requestedProgress, 0, 1)));
            audioEngine.Seek(target);
            if (videoPlayer.CurrentPath is not null)
            {
                var videoTime = toVideoTime(target);
                videoPlayer.Seek(videoTime < TimeSpan.Zero ? TimeSpan.Zero : videoTime);
                lastVideoResync = DateTime.UtcNow;
            }

            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            reportPlaybackError(exception);
        }
    }

    private static bool sameInstallation(OsuInstallation left, OsuInstallation right) =>
        left.Kind == right.Kind && string.Equals(left.RootPath, right.RootPath, StringComparison.OrdinalIgnoreCase);

    private static string formatTime(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
}
