using System.Text.RegularExpressions;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Hitsounds;

public interface IHitsoundSampleSourceFactory
{
    /// <summary>Builds the sample lookup chain for one beatmap set using the known installations.</summary>
    HitsoundSampleResolver Create(UnifiedBeatmapSet set, IEnumerable<OsuInstallation> installations);

    /// <summary>Builds the sample lookup chain for one beatmap set with optional skin directory and hit sound source preference.</summary>
    HitsoundSampleResolver Create(
        UnifiedBeatmapSet set,
        IEnumerable<OsuInstallation> installations,
        string? skinDirectory,
        bool preferSkinHitsounds);
}

/// <summary>
/// Sample chain: beatmap files, then the skin the user selected in osu!stable and in
/// osu!lazer, then osu!lazer's bundled default samples (the argon set when the lazer
/// skin is argon-based). Nothing is ever written.
/// </summary>
public sealed partial class HitsoundSampleSourceFactory : IHitsoundSampleSourceFactory
{
    private const string classic_prefix = "osu.Game.Resources.Samples.Gameplay.";
    private const string argon_prefix = "osu.Game.Resources.Samples.Gameplay.Argon.";
    private const string argon_pro_prefix = "osu.Game.Resources.Samples.Gameplay.ArgonPro.";

    private readonly Func<string?> lazerResourcesLocator;
    private readonly ILazerSkinReader skinReader;
    private readonly Dictionary<string, ISampleFileSource> sharedSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LazerSkinData?> skinCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public HitsoundSampleSourceFactory()
        : this(ManagedResourceSampleFileSource.FindLazerResourcesAssembly)
    {
    }

    public HitsoundSampleSourceFactory(Func<string?> lazerResourcesLocator)
        : this(lazerResourcesLocator, new LazerRealmReader())
    {
    }

    internal HitsoundSampleSourceFactory(Func<string?> lazerResourcesLocator, ILazerSkinReader skinReader)
    {
        this.lazerResourcesLocator = lazerResourcesLocator ?? throw new ArgumentNullException(nameof(lazerResourcesLocator));
        this.skinReader = skinReader ?? throw new ArgumentNullException(nameof(skinReader));
    }

    public HitsoundSampleResolver Create(UnifiedBeatmapSet set, IEnumerable<OsuInstallation> installations) =>
        Create(set, installations, null, false);

    public HitsoundSampleResolver Create(
        UnifiedBeatmapSet set,
        IEnumerable<OsuInstallation> installations,
        string? skinDirectory,
        bool preferSkinHitsounds)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(installations);

        var fallbacks = new List<ISampleFileSource>();
        if (!string.IsNullOrWhiteSpace(skinDirectory) && Directory.Exists(skinDirectory))
        {
            fallbacks.Add(shared(skinDirectory, static path => new DirectorySampleFileSource(path)));
        }
        var resourcePrefix = classic_prefix;
        foreach (var installation in installations)
        {
            if (installation.Kind == OsuInstallationKind.Stable && OperatingSystem.IsWindows())
            {
                var skin = FindStableSkinDirectory(installation.RootPath);
                if (skin is not null)
                {
                    fallbacks.Add(shared(skin, static path => new DirectorySampleFileSource(path)));
                }
            }
            else if (installation.Kind == OsuInstallationKind.Lazer)
            {
                var skin = findLazerSkin(installation.RootPath);
                if (skin is null)
                {
                    continue;
                }

                if (skin.FileHashes.Count > 0)
                {
                    var filesPath = Path.Combine(installation.RootPath, "files");
                    fallbacks.Add(shared("lazer-skin:" + skin.Id, _ => new BeatmapSampleFileSource(new LazerHashFileResolver(filesPath, skin.FileHashes))));
                }

                resourcePrefix = ResourcePrefixForSkinName(skin.Name);
            }
        }

        var resources = lazerResourcesLocator();
        if (resources is not null)
        {
            fallbacks.Add(shared(resources + "|" + resourcePrefix, _ => new ManagedResourceSampleFileSource(resources, resourcePrefix)));
            if (resourcePrefix != classic_prefix)
            {
                fallbacks.Add(shared(resources + "|" + classic_prefix, _ => new ManagedResourceSampleFileSource(resources, classic_prefix)));
            }
        }

        return new HitsoundSampleResolver(set.Files is null ? null : new BeatmapSampleFileSource(set.Files), fallbacks, preferSkinHitsounds);
    }

    /// <summary>Reads <c>Skin = name</c> from the user's osu!stable config and returns that skin folder.</summary>
    public static string? FindStableSkinDirectory(string stableRoot)
    {
        try
        {
            var skinsRoot = Path.Combine(stableRoot, "Skins");
            if (!Directory.Exists(skinsRoot))
            {
                return null;
            }

            var configs = Directory.EnumerateFiles(stableRoot, "osu!.*.cfg")
                .OrderByDescending(static path => path.EndsWith($"osu!.{Environment.UserName}.cfg", StringComparison.OrdinalIgnoreCase))
                .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase);
            foreach (var config in configs)
            {
                foreach (var line in File.ReadLines(config))
                {
                    var match = SkinLinePattern().Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var name = match.Groups["name"].Value.Trim();
                    if (name.Length == 0 || name.Equals("Default", StringComparison.OrdinalIgnoreCase) || name.Contains("..", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    var directory = Path.Combine(skinsRoot, name);
                    return Directory.Exists(directory) ? directory : null;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return null;
    }

    /// <summary>Reads the active skin id from lazer's <c>game.ini</c>.</summary>
    public static Guid? FindLazerSkinId(string lazerRoot)
    {
        try
        {
            var config = Path.Combine(lazerRoot, "game.ini");
            if (!File.Exists(config))
            {
                return null;
            }

            foreach (var line in File.ReadLines(config))
            {
                var match = SkinLinePattern().Match(line);
                if (match.Success && Guid.TryParse(match.Groups["name"].Value.Trim(), out var id))
                {
                    return id;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return null;
    }

    /// <summary>Picks which bundled sample set matches a lazer skin name (argon skins ship their own sounds).</summary>
    public static string ResourcePrefixForSkinName(string? skinName)
    {
        if (string.IsNullOrWhiteSpace(skinName) || !skinName.Contains("argon", StringComparison.OrdinalIgnoreCase))
        {
            return classic_prefix;
        }

        return skinName.Contains("pro", StringComparison.OrdinalIgnoreCase) ? argon_pro_prefix : argon_prefix;
    }

    private LazerSkinData? findLazerSkin(string lazerRoot)
    {
        var skinId = FindLazerSkinId(lazerRoot);
        if (skinId is null)
        {
            return null;
        }

        var key = lazerRoot + "|" + skinId;
        lock (sync)
        {
            if (skinCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        LazerSkinData? skin = null;
        try
        {
            var realmPath = Path.Combine(lazerRoot, "client.realm");
            if (File.Exists(realmPath))
            {
                skin = skinReader.ReadSkin(realmPath, skinId.Value);
            }
        }
        catch (Exception exception) when (exception is Realms.Exceptions.RealmException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        lock (sync)
        {
            skinCache[key] = skin;
        }

        return skin;
    }

    private ISampleFileSource shared(string key, Func<string, ISampleFileSource> create)
    {
        lock (sync)
        {
            if (!sharedSources.TryGetValue(key, out var source))
            {
                source = create(key);
                sharedSources[key] = source;
            }

            return source;
        }
    }

    [GeneratedRegex(@"^\s*Skin\s*=\s*(?<name>.*?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SkinLinePattern();
}
