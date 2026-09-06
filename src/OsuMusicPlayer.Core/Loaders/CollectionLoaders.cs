using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OsuMusicPlayer.Core.Models;
using OsuParsers.Decoders;
using Realms.Exceptions;

namespace OsuMusicPlayer.Core.Loaders;

public interface ICollectionLoader
{
    OsuInstallationKind Kind { get; }

    Task<IReadOnlyList<BeatmapCollectionInfo>> LoadAsync(string installationPath, CancellationToken cancellationToken = default);
}

/// <summary>Reads osu!stable's <c>collection.db</c> (Windows only, read-only).</summary>
public sealed class OsuStableCollectionLoader(ILogger<OsuStableCollectionLoader>? logger = null) : ICollectionLoader
{
    private readonly ILogger<OsuStableCollectionLoader> logger = logger ?? NullLogger<OsuStableCollectionLoader>.Instance;

    public OsuInstallationKind Kind => OsuInstallationKind.Stable;

    public Task<IReadOnlyList<BeatmapCollectionInfo>> LoadAsync(string installationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult<IReadOnlyList<BeatmapCollectionInfo>>([]);
        }

        return Task.Run(() => load(installationPath, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<BeatmapCollectionInfo> load(string installationPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetFullPath(installationPath), "collection.db");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var database = DatabaseDecoder.DecodeCollection(stream);
            var result = new List<BeatmapCollectionInfo>();
            foreach (var collection in database.Collections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hashes = (collection.MD5Hashes ?? [])
                    .Where(static hash => !string.IsNullOrWhiteSpace(hash))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                result.Add(new BeatmapCollectionInfo(collection.Name ?? string.Empty, OsuInstallationKind.Stable, hashes));
            }

            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException or FormatException or OverflowException or ArgumentException or IndexOutOfRangeException)
        {
            logger.LogWarning(exception, "Could not read stable collections at {Path}.", path);
            return [];
        }
    }
}

/// <summary>Reads osu!lazer's <c>BeatmapCollection</c> objects from <c>client.realm</c>.</summary>
public sealed class OsuLazerCollectionLoader : ICollectionLoader
{
    private readonly ILazerCollectionReader reader;
    private readonly ILogger<OsuLazerCollectionLoader> logger;

    public OsuLazerCollectionLoader(ILogger<OsuLazerCollectionLoader>? logger = null)
        : this(new LazerRealmReader(), logger)
    {
    }

    internal OsuLazerCollectionLoader(ILazerCollectionReader reader, ILogger<OsuLazerCollectionLoader>? logger = null)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.logger = logger ?? NullLogger<OsuLazerCollectionLoader>.Instance;
    }

    public OsuInstallationKind Kind => OsuInstallationKind.Lazer;

    public async Task<IReadOnlyList<BeatmapCollectionInfo>> LoadAsync(string installationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);
        var realmPath = Path.Combine(Path.GetFullPath(installationPath), "client.realm");
        if (!File.Exists(realmPath))
        {
            return [];
        }

        try
        {
            var collections = await reader.ReadCollectionsAsync(realmPath, cancellationToken).ConfigureAwait(false);
            return collections
                .Select(static collection => new BeatmapCollectionInfo(
                    collection.Name,
                    OsuInstallationKind.Lazer,
                    collection.Md5Hashes.Where(static hash => !string.IsNullOrWhiteSpace(hash)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RealmException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(exception, "Could not read lazer collections at {RealmPath}.", realmPath);
            return [];
        }
    }
}
