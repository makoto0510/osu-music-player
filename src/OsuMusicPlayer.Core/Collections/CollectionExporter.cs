using OsuParsers.Database;
using OsuParsers.Database.Objects;

namespace OsuMusicPlayer.Core.Collections;

/// <summary>
/// Writes a <c>collection.db</c> that osu!stable can import. The file is always written to a
/// path the user chose; writing into an osu! installation is refused to honour the
/// read-only rule for game folders.
/// </summary>
public static class CollectionExporter
{
    private const int osu_version_stamp = 20250101;

    public static void Write(string path, string collectionName, IEnumerable<string> beatmapMd5Hashes, IEnumerable<string> forbiddenRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentNullException.ThrowIfNull(beatmapMd5Hashes);
        ArgumentNullException.ThrowIfNull(forbiddenRoots);

        var fullPath = Path.GetFullPath(path);
        foreach (var root in forbiddenRoots)
        {
            if (IsInside(fullPath, root))
            {
                throw new InvalidOperationException($"Refusing to write into an osu! installation: {root}");
            }
        }

        var hashes = beatmapMd5Hashes
            .Where(static hash => !string.IsNullOrWhiteSpace(hash))
            .Select(static hash => hash.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var collection = new Collection { Name = collectionName, Count = hashes.Count };
        collection.MD5Hashes.AddRange(hashes);
        var database = new CollectionDatabase { OsuVersion = osu_version_stamp, CollectionCount = 1 };
        database.Collections.Add(collection);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        database.Save(fullPath);
    }

    public static bool IsInside(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
