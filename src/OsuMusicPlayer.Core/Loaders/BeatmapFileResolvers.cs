using System.Text.RegularExpressions;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Loaders;

/// <summary>Resolves files inside an osu!stable song folder. Paths never escape the folder.</summary>
public sealed class FolderFileResolver : IBeatmapFileResolver
{
    private readonly string folderPath;

    public FolderFileResolver(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        this.folderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public IEnumerable<string> FileNames
    {
        get
        {
            try
            {
                return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(folderPath, path))
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return [];
            }
        }
    }

    public string? Resolve(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(folderPath, fileName));
            return fullPath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

/// <summary>Resolves osu!lazer file names through the SHA-256 hash store.</summary>
public sealed partial class LazerHashFileResolver : IBeatmapFileResolver
{
    private readonly string filesPath;
    private readonly IReadOnlyDictionary<string, string> hashes;

    public LazerHashFileResolver(string filesPath, IReadOnlyDictionary<string, string> fileHashes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filesPath);
        ArgumentNullException.ThrowIfNull(fileHashes);
        this.filesPath = filesPath;
        hashes = fileHashes is Dictionary<string, string> { Comparer: var comparer } && ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase)
            ? fileHashes
            : new Dictionary<string, string>(fileHashes, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<string> FileNames => hashes.Keys;

    public string? Resolve(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !hashes.TryGetValue(fileName.Replace('\\', '/'), out var hash) && !hashes.TryGetValue(fileName, out hash))
        {
            return null;
        }

        return ResolveHash(filesPath, hash);
    }

    /// <summary>
    /// Builds the path of a stored file in the current three-level layout without touching
    /// the disk. Use it for paths that are only opened later, such as .osu files.
    /// </summary>
    public static string? BuildHashPath(string filesPath, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || !HashPattern().IsMatch(hash))
        {
            return null;
        }

        hash = hash.ToLowerInvariant();
        return Path.Combine(filesPath, hash[..1], hash[..2], hash);
    }

    /// <summary>Locates a stored file by hash, supporting both the current and the documented layout.</summary>
    public static string? ResolveHash(string filesPath, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || !HashPattern().IsMatch(hash))
        {
            return null;
        }

        hash = hash.ToLowerInvariant();
        string[] candidates =
        [
            Path.Combine(filesPath, hash[..1], hash[..2], hash),
            Path.Combine(filesPath, hash[..2], hash),
            Path.Combine(filesPath, hash),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
