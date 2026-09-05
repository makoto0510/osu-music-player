using Realms;
using Realms.Exceptions;

namespace OsuMusicPlayer.Core.Loaders;

internal sealed record LazerBeatmapData(
    Guid Id,
    int OnlineId,
    string DifficultyName,
    double Bpm,
    double Length,
    string Tags,
    double DrainRate,
    double OverallDifficulty)
{
    public string RulesetShortName { get; init; } = "osu";
    public int RulesetOnlineId { get; init; }
    public string? Md5Hash { get; init; }

    /// <summary>SHA-256 of the .osu file; the file lives in the hash store under this name.</summary>
    public string? Hash { get; init; }
    public int PreviewTimeMs { get; init; } = -1;
    public double StarRating { get; init; }
    public double CircleSize { get; init; }
    public double ApproachRate { get; init; }
}

internal sealed record LazerBeatmapSetData(
    Guid Id,
    int OnlineId,
    string Title,
    string TitleUnicode,
    string Artist,
    string ArtistUnicode,
    string Creator,
    string AudioFileName,
    string BackgroundFileName,
    IReadOnlyDictionary<string, string> FileHashes,
    IReadOnlyList<LazerBeatmapData> Beatmaps);

internal interface ILazerRealmReader
{
    Task<IReadOnlyList<LazerBeatmapSetData>> ReadAsync(string realmPath, CancellationToken cancellationToken);
}

internal sealed class LazerRealmReader : ILazerRealmReader
{
    public Task<IReadOnlyList<LazerBeatmapSetData>> ReadAsync(string realmPath, CancellationToken cancellationToken) =>
        Task.Run(() => read(realmPath, cancellationToken), cancellationToken);

    private static IReadOnlyList<LazerBeatmapSetData> read(string realmPath, CancellationToken cancellationToken)
    {
        var configuration = new RealmConfiguration(realmPath)
        {
            IsReadOnly = true,
            IsDynamic = true,
        };

        cancellationToken.ThrowIfCancellationRequested();
        using var realm = Realm.GetInstance(configuration);
        var results = new List<LazerBeatmapSetData>();

        foreach (var set in realm.DynamicApi.All("BeatmapSet"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Protected sets are the bundled intro tracks that lazer hides from song select.
                if (get(set, "DeletePending", false) || get(set, "Protected", false))
                {
                    continue;
                }

                var beatmaps = new List<LazerBeatmapData>();
                string title = string.Empty;
                string titleUnicode = string.Empty;
                string artist = string.Empty;
                string artistUnicode = string.Empty;
                string creator = string.Empty;
                string audioFile = string.Empty;
                string backgroundFile = string.Empty;

                foreach (var beatmap in set.DynamicApi.GetList<IRealmObjectBase>("Beatmaps"))
                {
                    try
                    {
                        var metadata = beatmap.DynamicApi.Get<IRealmObjectBase>("Metadata");
                        var difficulty = beatmap.DynamicApi.Get<IRealmObjectBase>("Difficulty");
                        title = get(metadata, "Title", title);
                        titleUnicode = get(metadata, "TitleUnicode", titleUnicode);
                        artist = get(metadata, "Artist", artist);
                        artistUnicode = get(metadata, "ArtistUnicode", artistUnicode);
                        audioFile = get(metadata, "AudioFile", audioFile);
                        backgroundFile = get(metadata, "BackgroundFile", backgroundFile);
                        var tags = get(metadata, "Tags", string.Empty);

                        var author = tryGetObject(metadata, "Author");
                        if (author is not null)
                        {
                            creator = get(author, "Username", creator);
                        }

                        var ruleset = tryGetObject(beatmap, "Ruleset");

                        beatmaps.Add(new LazerBeatmapData(
                            get(beatmap, "ID", Guid.Empty),
                            get(beatmap, "OnlineID", -1),
                            get(beatmap, "DifficultyName", string.Empty),
                            get(beatmap, "BPM", 0d),
                            get(beatmap, "Length", 0d),
                            tags,
                            getNumeric(difficulty, "DrainRate"),
                            getNumeric(difficulty, "OverallDifficulty"))
                        {
                            RulesetShortName = ruleset is null ? "osu" : get(ruleset, "ShortName", "osu"),
                            RulesetOnlineId = ruleset is null ? 0 : get(ruleset, "OnlineID", 0),
                            Md5Hash = get<string?>(beatmap, "MD5Hash", null),
                            Hash = get<string?>(beatmap, "Hash", null),
                            PreviewTimeMs = get(metadata, "PreviewTime", -1),
                            StarRating = get(beatmap, "StarRating", 0d),
                            CircleSize = getNumeric(difficulty, "CircleSize"),
                            ApproachRate = getNumeric(difficulty, "ApproachRate"),
                        });
                    }
                    catch (Exception exception) when (isSchemaOrDataError(exception))
                    {
                        // A malformed linked beatmap must not discard the remaining set.
                    }
                }

                if (beatmaps.Count == 0)
                {
                    continue;
                }

                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var usage in set.DynamicApi.GetList<IRealmObjectBase>("Files"))
                {
                    try
                    {
                        var filename = get(usage, "Filename", string.Empty);
                        var file = usage.DynamicApi.Get<IRealmObjectBase>("File");
                        var hash = get(file, "Hash", string.Empty);
                        if (!string.IsNullOrWhiteSpace(filename) && !string.IsNullOrWhiteSpace(hash))
                        {
                            files[filename] = hash;
                        }
                    }
                    catch (Exception exception) when (isSchemaOrDataError(exception))
                    {
                        // Ignore a corrupt file usage and continue resolving other assets.
                    }
                }

                results.Add(new LazerBeatmapSetData(
                    get(set, "ID", Guid.Empty),
                    get(set, "OnlineID", -1),
                    title,
                    titleUnicode,
                    artist,
                    artistUnicode,
                    creator,
                    audioFile,
                    backgroundFile,
                    files,
                    beatmaps));
            }
            catch (Exception exception) when (isSchemaOrDataError(exception))
            {
                // Schema drift or one corrupt set must not abort the complete library scan.
            }
        }

        return results;
    }

    private static IRealmObjectBase? tryGetObject(IRealmObjectBase source, string propertyName)
    {
        try
        {
            return source.DynamicApi.Get<IRealmObjectBase>(propertyName);
        }
        catch (Exception exception) when (isSchemaOrDataError(exception))
        {
            return null;
        }
    }

    private static T get<T>(IRealmObjectBase source, string propertyName, T fallback)
    {
        try
        {
            return source.DynamicApi.Get<T>(propertyName) ?? fallback;
        }
        catch (Exception exception) when (isSchemaOrDataError(exception))
        {
            return fallback;
        }
    }

    private static double getNumeric(IRealmObjectBase source, string propertyName)
    {
        try
        {
            // Current osu!lazer stores difficulty values as Realm Float fields.
            // Reading them as Double first creates thousands of failed dynamic
            // accessors and can trigger a Realm SDK finalizer defect.
            return source.DynamicApi.Get<float>(propertyName);
        }
        catch (Exception exception) when (isSchemaOrDataError(exception))
        {
            try
            {
                return source.DynamicApi.Get<double>(propertyName);
            }
            catch (Exception nested) when (isSchemaOrDataError(nested))
            {
                return 0;
            }
        }
    }

    private static bool isSchemaOrDataError(Exception exception) => exception is
        RealmException or
        InvalidCastException or
        ArgumentException or
        InvalidOperationException or
        NullReferenceException;
}
