namespace OsuMusicPlayer.Core.Hitsounds;

/// <summary>A place that can hold hit sound samples: a beatmap, a skin folder or the game's default set.</summary>
public interface ISampleFileSource
{
    /// <summary>
    /// Reads the sample called <paramref name="name"/>. The name may or may not carry an
    /// extension; sources try the audio extensions osu! accepts. Returns <see langword="null"/>
    /// when the sample does not exist here.
    /// </summary>
    byte[]? Read(string name);
}

/// <summary>Samples stored with a beatmap set (song folder or lazer hash store).</summary>
public sealed class BeatmapSampleFileSource(Models.IBeatmapFileResolver files) : ISampleFileSource
{
    private readonly Models.IBeatmapFileResolver files = files ?? throw new ArgumentNullException(nameof(files));

    public byte[]? Read(string name)
    {
        foreach (var candidate in SampleFileNames.Candidates(name))
        {
            var path = files.Resolve(candidate);
            if (path is not null)
            {
                return SampleFileNames.TryReadFile(path);
            }
        }

        return null;
    }
}

/// <summary>Samples inside a plain folder, such as an osu!stable skin.</summary>
public sealed class DirectorySampleFileSource : ISampleFileSource
{
    private readonly string directory;
    private readonly Lazy<Dictionary<string, string>> index;

    public DirectorySampleFileSource(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        index = new Lazy<Dictionary<string, string>>(buildIndex);
    }

    public byte[]? Read(string name)
    {
        foreach (var candidate in SampleFileNames.Candidates(name))
        {
            if (index.Value.TryGetValue(candidate, out var path))
            {
                return SampleFileNames.TryReadFile(path);
            }
        }

        return null;
    }

    private Dictionary<string, string> buildIndex()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                result.TryAdd(Path.GetFileName(path), path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // An unreadable skin folder just contributes nothing.
        }

        return result;
    }
}

internal static class SampleFileNames
{
    private static readonly string[] extensions = [".wav", ".ogg", ".mp3"];

    public static IEnumerable<string> Candidates(string name)
    {
        if (Path.HasExtension(name))
        {
            yield return name;
        }

        foreach (var extension in extensions)
        {
            yield return name + extension;
        }
    }

    public static byte[]? TryReadFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
