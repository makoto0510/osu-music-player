using System.Text.RegularExpressions;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Hitsounds;

public interface IHitsoundSampleSourceFactory
{
    /// <summary>Builds the sample lookup chain for one beatmap set using the known installations.</summary>
    HitsoundSampleResolver Create(UnifiedBeatmapSet set, IEnumerable<OsuInstallation> installations);
}

/// <summary>
/// Sample chain: beatmap files, then the osu!stable skin currently selected in the
/// user's config, then osu!lazer's bundled default samples. Nothing is ever written.
/// </summary>
public sealed partial class HitsoundSampleSourceFactory : IHitsoundSampleSourceFactory
{
    private readonly Func<string?> lazerResourcesLocator;
    private readonly Dictionary<string, ISampleFileSource> sharedSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public HitsoundSampleSourceFactory()
        : this(ManagedResourceSampleFileSource.FindLazerResourcesAssembly)
    {
    }

    public HitsoundSampleSourceFactory(Func<string?> lazerResourcesLocator)
    {
        this.lazerResourcesLocator = lazerResourcesLocator ?? throw new ArgumentNullException(nameof(lazerResourcesLocator));
    }

    public HitsoundSampleResolver Create(UnifiedBeatmapSet set, IEnumerable<OsuInstallation> installations)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(installations);

        var fallbacks = new List<ISampleFileSource>();
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
        }

        var resources = lazerResourcesLocator();
        if (resources is not null)
        {
            fallbacks.Add(shared(resources, static path => new ManagedResourceSampleFileSource(path)));
        }

        return new HitsoundSampleResolver(set.Files is null ? null : new BeatmapSampleFileSource(set.Files), fallbacks);
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
