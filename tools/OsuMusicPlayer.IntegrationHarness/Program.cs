using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

// Integration harness against the real osu! installations on this machine.
//
//   osu-music-harness [load|audio|hitsounds|collections|realm|all] [--lazer <folder>] [--stable <folder>]
//
// load        loads every installation, prints counts and anomalies, then merges
// collections reads collection.db / lazer collections and reports how many members resolve to loaded sets
// audio      plays a few seconds of a stable and a lazer track through BASS (needs the natives)
// hitsounds  builds the hit sound timeline of a few maps and reports unresolved samples
// realm      dumps the lazer Realm schema (run this when a new lazer release changes client.realm)
// all        everything above

using var loggerFactory = LoggerFactory.Create(static builder => builder.AddSimpleConsole(static options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(LogLevel.Debug));
var log = loggerFactory.CreateLogger("harness");
var mode = args.FirstOrDefault(static argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? "load";

var locator = new OsuInstallationLocator();
var installations = new List<OsuInstallation>(locator.FindInstallations());
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--lazer" or "--stable")
    {
        var kind = args[i] == "--lazer" ? OsuInstallationKind.Lazer : OsuInstallationKind.Stable;
        var validated = locator.ValidateManualPath(args[i + 1], kind);
        if (validated is null)
        {
            log.LogError("{Kind} folder is not valid: {Path}", kind, args[i + 1]);
            return 1;
        }

        installations.Add(validated);
    }
}

log.LogInformation("Installations: {Count}", installations.Count);
foreach (var installation in installations)
{
    log.LogInformation("  {Kind}: {Path}", installation.Kind, installation.RootPath);
}

if (installations.Count == 0)
{
    log.LogError("No osu! installation found. Pass --lazer <folder> or --stable <folder>.");
    return 1;
}

if (mode is "realm" or "all")
{
    foreach (var lazer in installations.Where(static installation => installation.Kind == OsuInstallationKind.Lazer))
    {
        dumpRealmSchema(Path.Combine(lazer.RootPath, "client.realm"));
    }

    if (mode == "realm")
    {
        return 0;
    }
}

var stableLoader = new OsuStableLoader(loggerFactory.CreateLogger<OsuStableLoader>());
var lazerLoader = new OsuLazerLoader(loggerFactory.CreateLogger<OsuLazerLoader>());
var perInstallation = new Dictionary<OsuInstallationKind, IReadOnlyList<UnifiedBeatmapSet>>();
foreach (var installation in installations)
{
    IBeatmapLoader loader = installation.Kind == OsuInstallationKind.Stable ? stableLoader : lazerLoader;
    var stopwatch = Stopwatch.StartNew();
    var sets = await loader.LoadAsync(installation.RootPath);
    perInstallation[installation.Kind] = sets;
    report(installation.Kind.ToString(), sets, stopwatch.Elapsed);
}

var manager = new BeatmapManager([stableLoader, lazerLoader], new DuplicateDetector());
var mergeStopwatch = Stopwatch.StartNew();
var merged = await manager.LoadAsync(installations);
report("MERGED", merged, mergeStopwatch.Elapsed);
foreach (var group in merged.GroupBy(static set => set.Source))
{
    log.LogInformation("  merged source {Source}: {Count}", group.Key, group.Count());
}

if (mode is "collections" or "all")
{
    var byHash = new Dictionary<string, UnifiedBeatmapSet>(StringComparer.OrdinalIgnoreCase);
    foreach (var set in merged)
    {
        foreach (var hash in set.Beatmaps.SelectMany(static beatmap => beatmap.AllMd5Hashes))
        {
            byHash.TryAdd(hash, set);
        }
    }

    log.LogInformation("Hash index: {Hashes} hashes over {Sets} sets", byHash.Count, merged.Count);
    foreach (var installation in installations)
    {
        ICollectionLoader collectionLoader = installation.Kind == OsuInstallationKind.Stable
            ? new OsuStableCollectionLoader(loggerFactory.CreateLogger<OsuStableCollectionLoader>())
            : new OsuLazerCollectionLoader(loggerFactory.CreateLogger<OsuLazerCollectionLoader>());
        var collections = await collectionLoader.LoadAsync(installation.RootPath);
        log.LogInformation("{Kind}: {Count} collections", installation.Kind, collections.Count);
        foreach (var collection in collections)
        {
            var resolved = collection.BeatmapMd5Hashes.Where(byHash.ContainsKey).Select(hash => byHash[hash]).Distinct().Count();
            var unresolved = collection.BeatmapMd5Hashes.Where(hash => !byHash.ContainsKey(hash)).ToArray();
            log.LogInformation("  {Name}: {Hashes} hashes -> {Sets} sets, {Unresolved} unresolved{Sample}",
                collection.Name, collection.BeatmapMd5Hashes.Count, resolved, unresolved.Length,
                unresolved.Length == 0 ? string.Empty : " (e.g. " + string.Join(", ", unresolved.Take(2)) + ")");
        }
    }

    if (mode == "collections")
    {
        return 0;
    }
}

if (mode is "hitsounds" or "all")
{
    var factory = new HitsoundSampleSourceFactory();
    foreach (var set in merged.Where(static set => set.Beatmaps.Any(static beatmap => beatmap.BeatmapFilePath is not null)).Take(5))
    {
        var beatmap = set.Beatmaps.First(static beatmap => beatmap.BeatmapFilePath is not null);
        try
        {
            var events = HitsoundTimelineBuilder.Build(beatmap.BeatmapFilePath!);
            var resolver = factory.Create(set, installations);
            var missing = events.SelectMany(static hit => hit.Samples).DistinctBy(HitsoundSampleResolver.CacheKey).Count(sample => resolver.Resolve(sample) is null);
            log.LogInformation("HITSOUNDS {Artist} - {Title} [{Diff}]: {Events} events, {Missing} unresolved samples", set.Artist, set.Title, beatmap.DifficultyName, events.Count, missing);
        }
        catch (Exception exception)
        {
            log.LogWarning(exception, "HITSOUNDS failed for {Path}", beatmap.BeatmapFilePath);
        }
    }
}

if (mode is "audio" or "all")
{
    var candidates = merged.Where(static set => set.AudioFilePath is not null).ToList();
    using var engine = new BassAudioEngine(loggerFactory.CreateLogger<BassAudioEngine>());
    engine.Volume = 0.15f;
    foreach (var source in new[] { BeatmapSource.Stable, BeatmapSource.Lazer, BeatmapSource.Both })
    {
        var pick = candidates.Where(set => set.Source == source).Skip(candidates.Count(set => set.Source == source) / 2).FirstOrDefault();
        if (pick is null)
        {
            continue;
        }

        log.LogInformation("AUDIO [{Source}] {Artist} - {Title} -> {Path}", pick.Source, pick.Artist, pick.Title, pick.AudioFilePath);
        try
        {
            await engine.LoadAsync(pick.AudioFilePath!);
            engine.Play();
            await Task.Delay(1500);
            log.LogInformation("  state={State} pos={Position} total={Total}", engine.State, engine.CurrentTime, engine.TotalTime);
            engine.Mod = OsuAudioMod.DT;
            await Task.Delay(1000);
            log.LogInformation("  DT pos={Position}", engine.CurrentTime);
            engine.Mod = OsuAudioMod.None;
            engine.Stop();
        }
        catch (AudioEngineException exception)
        {
            log.LogError(exception, "  playback failed (are the BASS natives for this platform in place?)");
        }
    }
}

return 0;

void report(string label, IReadOnlyList<UnifiedBeatmapSet> sets, TimeSpan elapsed)
{
    var diffs = sets.Sum(static set => set.Beatmaps.Count);
    var noBackground = sets.Count(static set => set.BackgroundFilePath is null);
    var noTitle = sets.Count(static set => string.IsNullOrWhiteSpace(set.Title));
    var zeroLength = sets.Count(static set => set.Beatmaps.All(static beatmap => beatmap.Length <= TimeSpan.Zero));
    var zeroBpm = sets.Count(static set => set.Beatmaps.All(static beatmap => beatmap.BPM <= 0));
    var missingAudio = sets.Count(static set => set.AudioFilePath is not null && !File.Exists(set.AudioFilePath));
    log.LogInformation("{Label}: {Sets} sets / {Diffs} diffs in {Elapsed}; noBackground={NoBackground} noTitle={NoTitle} zeroLength={ZeroLength} zeroBpm={ZeroBpm} audioMissingOnDisk={Missing}",
        label, sets.Count, diffs, elapsed, noBackground, noTitle, zeroLength, zeroBpm, missingAudio);
    foreach (var set in sets.Where(static set => set.Beatmaps.Max(static beatmap => beatmap.BPM) > 300).Take(3))
    {
        log.LogInformation("  high BPM: {Artist} - {Title}: {Bpm:0}", set.Artist, set.Title, set.Beatmaps.Max(static beatmap => beatmap.BPM));
    }
}

void dumpRealmSchema(string realmPath)
{
    if (!File.Exists(realmPath))
    {
        log.LogWarning("Realm not found: {Path}", realmPath);
        return;
    }

    try
    {
        var configuration = new Realms.RealmConfiguration(realmPath) { IsReadOnly = true, IsDynamic = true };
        using var realm = Realms.Realm.GetInstance(configuration);
        log.LogInformation("REALM {Path}", realmPath);
        foreach (var objectSchema in realm.Schema.OrderBy(static schema => schema.Name))
        {
            log.LogInformation("  {Name}: {Properties}", objectSchema.Name, string.Join(", ", objectSchema.Select(static property => property.Name + ":" + property.Type)));
        }
    }
    catch (Exception exception)
    {
        log.LogError(exception, "Could not open the Realm; the file format may be newer than the Realm SDK in use.");
    }
}
