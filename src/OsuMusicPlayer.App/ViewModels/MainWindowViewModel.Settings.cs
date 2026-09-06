using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Settings persistence, the equalizer and restoring the last playback state.</summary>
public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan save_debounce = TimeSpan.FromMilliseconds(600);

    private static readonly HashSet<string> persisted_properties =
    [
        nameof(Volume), nameof(Mod), nameof(IsShuffleEnabled), nameof(RepeatMode), nameof(SelectedSort),
        nameof(IsHitsoundEnabled), nameof(IsStoryboardEnabled), nameof(IsVideoEnabled),
        nameof(HitsoundVolume), nameof(HitsoundOffsetMs), nameof(IsServerEnabled), nameof(ServerPort), nameof(ServerAllowRemote),
        nameof(IsRichPresenceEnabled), nameof(DiscordApplicationId), nameof(OsuApiClientId), nameof(OsuApiClientSecret),
        nameof(ExcludeMinLengthSeconds), nameof(ExcludeMaxLengthSeconds), nameof(ExcludeQueryText),
        nameof(SelectedThemeName), nameof(AccentColorText),
    ];

    private readonly object saveSync = new();
    private CancellationTokenSource? saveDebounce;
    private AppSettings? loadedSettings;
    private bool restoringSettings;
    private bool applyingPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EqualizerPresetText))]
    private string? selectedEqualizerPreset = "Flat";

    [ObservableProperty]
    private bool isEqualizerPanelVisible;

    [ObservableProperty]
    private bool isSettingsPanelVisible;

    [ObservableProperty]
    private double hitsoundVolume = 1;

    [ObservableProperty]
    private int hitsoundOffsetMs;

    [ObservableProperty]
    private bool isServerEnabled;

    [ObservableProperty]
    private int serverPort = 5150;

    [ObservableProperty]
    private bool serverAllowRemote = true;

    [ObservableProperty]
    private bool isRichPresenceEnabled;

    [ObservableProperty]
    private string discordApplicationId = string.Empty;

    [ObservableProperty]
    private string osuApiClientId = string.Empty;

    [ObservableProperty]
    private string osuApiClientSecret = string.Empty;

    public ObservableCollection<EqualizerBandViewModel> EqualizerBands { get; } = [];
    public IReadOnlyList<string> EqualizerPresetNames => Equalizer.PresetNames;
    public string EqualizerPresetText => SelectedEqualizerPreset ?? "Custom";
    public string HitsoundOffsetText => $"{HitsoundOffsetMs:+0;-0;0} ms";

    /// <summary>Waits for a pending debounced save; used by tests.</summary>
    internal Task PendingSave { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void ResetEqualizer() => SelectedEqualizerPreset = "Flat";

    partial void OnSelectedEqualizerPresetChanged(string? value)
    {
        if (value is null || applyingPreset || !Equalizer.Presets.TryGetValue(value, out var gains))
        {
            return;
        }

        applyingPreset = true;
        try
        {
            for (var i = 0; i < EqualizerBands.Count; i++)
            {
                EqualizerBands[i].Suppress = true;
                EqualizerBands[i].Gain = gains[i];
                EqualizerBands[i].Suppress = false;
            }
        }
        finally
        {
            applyingPreset = false;
        }

        applyEqualizer();
        RequestSettingsSave();
    }

    partial void OnHitsoundVolumeChanged(double value) => hitsoundPlayer.Volume = (float)Math.Clamp(value, 0, 1);

    partial void OnHitsoundOffsetMsChanged(int value)
    {
        hitsoundPlayer.OffsetMs = value;
        OnPropertyChanged(nameof(HitsoundOffsetText));
    }

    private void initializePersistence()
    {
        for (var i = 0; i < Equalizer.BandCount; i++)
        {
            EqualizerBands.Add(new EqualizerBandViewModel(i, Equalizer.BandLabels[i], Equalizer.CenterFrequencies[i], onBandChanged));
        }

        PropertyChanged += onPersistedPropertyChanged;
    }

    private void onPersistedPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is { } name && persisted_properties.Contains(name))
        {
            RequestSettingsSave();
        }
    }

    private void onBandChanged(EqualizerBandViewModel band)
    {
        if (applyingPreset)
        {
            return;
        }

        applyEqualizer();
        applyingPreset = true;
        try
        {
            SelectedEqualizerPreset = Equalizer.FindPresetName(currentGains());
        }
        finally
        {
            applyingPreset = false;
        }

        RequestSettingsSave();
    }

    private float[] currentGains() => EqualizerBands.Select(static band => (float)band.Gain).ToArray();

    private void applyEqualizer()
    {
        try
        {
            audioEngine.SetEqualizer(currentGains());
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Coalesces rapid changes (sliders) into one write a moment later.</summary>
    internal void RequestSettingsSave()
    {
        if (restoringSettings || disposed || !initialized)
        {
            return;
        }

        CancellationTokenSource source;
        lock (saveSync)
        {
            saveDebounce?.Cancel();
            saveDebounce?.Dispose();
            saveDebounce = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            source = saveDebounce;
        }

        PendingSave = saveLaterAsync(source.Token);
    }

    private async Task saveLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(save_debounce, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await saveSettingsAsync().ConfigureAwait(false);
    }

    internal AppSettings BuildSettings()
    {
        var current = CurrentTrack;
        return new AppSettings
        {
            ManualInstallations = manualInstallations
                .Select(static installation => new ManualInstallationSetting(installation.Kind, installation.RootPath))
                .ToArray(),
            Playlists = Playlists.Select(static playlist => new PlaylistSetting(playlist.Id, playlist.Name, playlist.TrackIds.ToArray())).ToArray(),
            SmartPlaylists = SmartPlaylists.ToArray(),
            Favourites = favouriteIds.ToArray(),
            Volume = Volume,
            Mod = Mod,
            Shuffle = IsShuffleEnabled,
            Repeat = RepeatMode,
            Sort = SelectedSort,
            HitsoundsEnabled = IsHitsoundEnabled,
            HitsoundVolume = HitsoundVolume,
            HitsoundOffsetMs = HitsoundOffsetMs,
            StoryboardEnabled = IsStoryboardEnabled,
            VideoEnabled = IsVideoEnabled,
            EqualizerGains = currentGains(),
            LastPlayback = current is null
                ? null
                : new PlaybackStateSetting(current.Model.Id, safeCurrentTime().TotalSeconds, Queue.Select(static track => track.Model.Id).ToArray()),
            PlayHistory = playHistory.Values.ToArray(),
            Server = new ServerSettings { Enabled = IsServerEnabled, Port = ServerPort, AllowRemoteConnections = ServerAllowRemote },
            RichPresenceEnabled = IsRichPresenceEnabled,
            DiscordApplicationId = DiscordApplicationId,
            OsuApiClientId = OsuApiClientId,
            OsuApiClientSecret = OsuApiClientSecret,
            Appearance = new AppearanceSettings { ThemeName = SelectedThemeName, AccentColor = AccentColorText.Trim() },
            Exclusions = new LibraryExclusionSettings
            {
                MinimumLengthSeconds = Math.Max(0, ExcludeMinLengthSeconds),
                MaximumLengthSeconds = Math.Max(0, ExcludeMaxLengthSeconds),
                ExcludeQuery = ExcludeQueryText.Trim(),
            },
        };
    }

    /// <summary>Synchronous save for shutdown, when no async continuation would run any more.</summary>
    internal void SaveSettingsNow()
    {
        if (!initialized)
        {
            return;
        }

        try
        {
            settingsStore.SaveAsync(BuildSettings(), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Losing the last position at shutdown is not worth crashing over.
        }
    }

    private void applyRestoredSettings(AppSettings settings)
    {
        restoringSettings = true;
        try
        {
            Volume = Math.Clamp(settings.Volume, 0, 1);
            Mod = Enum.IsDefined(settings.Mod) ? settings.Mod : OsuAudioMod.None;
            IsShuffleEnabled = settings.Shuffle;
            RepeatMode = Enum.IsDefined(settings.Repeat) ? settings.Repeat : RepeatMode.Off;
            SelectedSort = Enum.IsDefined(settings.Sort) ? settings.Sort : TrackSortOption.Title;
            HitsoundVolume = Math.Clamp(settings.HitsoundVolume, 0, 1);
            HitsoundOffsetMs = Math.Clamp(settings.HitsoundOffsetMs, -500, 500);
            IsHitsoundEnabled = settings.HitsoundsEnabled;
            IsStoryboardEnabled = settings.StoryboardEnabled;
            if (videoPlayer.IsAvailable)
            {
                IsVideoEnabled = settings.VideoEnabled;
            }

            IsServerEnabled = settings.Server.Enabled;
            ServerPort = settings.Server.Port is > 0 and < 65536 ? settings.Server.Port : 5150;
            ServerAllowRemote = settings.Server.AllowRemoteConnections;
            IsRichPresenceEnabled = settings.RichPresenceEnabled;
            DiscordApplicationId = settings.DiscordApplicationId ?? string.Empty;
            OsuApiClientId = settings.OsuApiClientId ?? string.Empty;
            OsuApiClientSecret = settings.OsuApiClientSecret ?? string.Empty;
            applyRestoredExclusions(settings);
            applyRestoredAppearance(settings);

            var gains = Equalizer.Normalize(settings.EqualizerGains);
            applyingPreset = true;
            try
            {
                for (var i = 0; i < EqualizerBands.Count; i++)
                {
                    EqualizerBands[i].Suppress = true;
                    EqualizerBands[i].Gain = gains[i];
                    EqualizerBands[i].Suppress = false;
                }

                SelectedEqualizerPreset = Equalizer.FindPresetName(gains);
            }
            finally
            {
                applyingPreset = false;
            }

            applyEqualizer();
            applyRestoredHistory(settings);
            applyRestoredLibrary(settings);
        }
        finally
        {
            restoringSettings = false;
        }
    }

    /// <summary>Puts the last track back into the player, paused at its old position, and restores the queue.</summary>
    private async Task restorePlaybackAsync(AppSettings settings)
    {
        if (settings.LastPlayback is not { } state || disposed)
        {
            return;
        }

        TrackItemViewModel? track = null;
        List<TrackItemViewModel> queued = [];
        await dispatcher.InvokeAsync(() =>
        {
            track = tracksById.GetValueOrDefault(state.TrackId);
            queued = state.QueueIds.Select(id => tracksById.GetValueOrDefault(id)).Where(static item => item is not null).ToList()!;
        }).ConfigureAwait(false);

        if (track?.Model.AudioFilePath is not { Length: > 0 } audioPath)
        {
            return;
        }

        var version = Interlocked.Increment(ref loadVersion);
        try
        {
            await audioEngine.LoadAsync(audioPath).ConfigureAwait(false);
            if (version != Volatile.Read(ref loadVersion) || disposed)
            {
                return;
            }

            var position = TimeSpan.FromSeconds(Math.Max(0, state.PositionSeconds));
            if (position > TimeSpan.Zero && position < audioEngine.TotalTime)
            {
                audioEngine.Seek(position);
            }

            await dispatcher.InvokeAsync(() =>
            {
                CurrentTrack = track;
                SelectedTrack = track;
                IsPlaying = false;
                CurrentMedia = Core.BeatmapMedia.None;
                foreach (var item in queued)
                {
                    Queue.Add(item);
                }

                notifyQueueChanged();
                updatePosition(audioEngine.CurrentTime, audioEngine.TotalTime);
            }).ConfigureAwait(false);
            _ = resolveMediaAsync(track, version);
        }
        catch (Exception exception) when (exception is AudioEngineException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The file may have moved since last time; simply start without a track.
        }
    }

    private TimeSpan safeCurrentTime()
    {
        try
        {
            return audioEngine.CurrentTime;
        }
        catch (ObjectDisposedException)
        {
            return TimeSpan.Zero;
        }
    }
}
