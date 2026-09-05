using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OsuMusicPlayer.Core.Hitsounds;

/// <summary>
/// Reads samples embedded as manifest resources in a managed assembly, such as osu!lazer's
/// <c>osu.Game.Resources.dll</c>, which carries the default hit sounds. The file is parsed
/// with the PE reader only; no code from it is ever loaded or executed.
/// </summary>
public sealed class ManagedResourceSampleFileSource : ISampleFileSource
{
    private const string default_prefix = "osu.Game.Resources.Samples.Gameplay.";

    private readonly string assemblyPath;
    private readonly string prefix;
    private readonly Lazy<Dictionary<string, byte[]>> resources;

    public ManagedResourceSampleFileSource(string assemblyPath, string resourcePrefix = default_prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        this.assemblyPath = assemblyPath;
        prefix = resourcePrefix ?? string.Empty;
        resources = new Lazy<Dictionary<string, byte[]>>(load);
    }

    /// <summary>Where osu!lazer keeps its resources assembly on this platform, or <see langword="null"/>.</summary>
    public static string? FindLazerResourcesAssembly()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                candidates.Add(Path.Combine(local, "osulazer", "current", "osu.Game.Resources.dll"));
                try
                {
                    var root = Path.Combine(local, "osulazer");
                    if (Directory.Exists(root))
                    {
                        candidates.AddRange(Directory.EnumerateDirectories(root, "app-*").OrderDescending(StringComparer.Ordinal).Select(dir => Path.Combine(dir, "osu.Game.Resources.dll")));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/Applications/osu!.app/Contents/MacOS/osu.Game.Resources.dll");
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public byte[]? Read(string name)
    {
        foreach (var candidate in SampleFileNames.Candidates(name))
        {
            if (resources.Value.TryGetValue(prefix + candidate, out var bytes))
            {
                return bytes;
            }
        }

        return null;
    }

    private Dictionary<string, byte[]> load()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new PEReader(stream);
            if (!reader.HasMetadata || reader.PEHeaders.CorHeader is null)
            {
                return result;
            }

            var metadata = reader.GetMetadataReader();
            var section = reader.GetSectionData(reader.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress);
            foreach (var handle in metadata.ManifestResources)
            {
                var resource = metadata.GetManifestResource(handle);
                if (!resource.Implementation.IsNil)
                {
                    continue; // lives in another file
                }

                var name = metadata.GetString(resource.Name);
                if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var offset = checked((int)resource.Offset);
                if (offset < 0 || offset + 4 > section.Length)
                {
                    continue;
                }

                var blob = section.GetReader(offset, section.Length - offset);
                var length = blob.ReadInt32();
                if (length < 0 || length > blob.RemainingBytes)
                {
                    continue;
                }

                result[name] = blob.ReadBytes(length);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or OverflowException)
        {
            // A missing or damaged resources assembly only removes the default samples.
        }

        return result;
    }
}
