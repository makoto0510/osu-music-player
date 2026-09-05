using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OsuMusicPlayer.Core.Models;
using ReOsuStoryboardPlayer.Core.Base;
using ReOsuStoryboardPlayer.Core.Parser.Collection;
using ReOsuStoryboardPlayer.Core.Parser.Reader;
using ReOsuStoryboardPlayer.Core.Parser.Stream;
using SkiaSharp;

namespace OsuMusicPlayer.App.Services;

public interface IStoryboardLoader
{
    /// <summary>
    /// Parses the set's .osb (if any) and the selected difficulty's .osu storyboard events and
    /// decodes every referenced image. Returns <see langword="null"/> when there is nothing to show.
    /// </summary>
    Task<StoryboardSession?> LoadAsync(string? storyboardFilePath, string? beatmapFilePath, IBeatmapFileResolver files, CancellationToken cancellationToken = default);
}

/// <summary>Builds storyboard sessions with the MIT-licensed ReOsuStoryboardPlayer.Core parser.</summary>
public sealed class StoryboardLoader(ILogger<StoryboardLoader>? logger = null) : IStoryboardLoader
{
    private const long max_texture_bytes = 512L * 1024 * 1024;
    private static readonly string[] image_extensions = [".png", ".jpg", ".jpeg"];

    private readonly ILogger<StoryboardLoader> logger = logger ?? NullLogger<StoryboardLoader>.Instance;

    static StoryboardLoader()
    {
        ReOsuStoryboardPlayer.Setting.AllowLog = false;
    }

    public Task<StoryboardSession?> LoadAsync(string? storyboardFilePath, string? beatmapFilePath, IBeatmapFileResolver files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        return Task.Run(() => load(storyboardFilePath, beatmapFilePath, files, cancellationToken), cancellationToken);
    }

    private StoryboardSession? load(string? storyboardFilePath, string? beatmapFilePath, IBeatmapFileResolver files, CancellationToken cancellationToken)
    {
        var objects = new List<StoryboardObject>();
        if (storyboardFilePath is not null && File.Exists(storyboardFilePath))
        {
            objects.AddRange(parse(storyboardFilePath, fromOsb: true, cancellationToken));
        }

        var widescreen = false;
        if (beatmapFilePath is not null && File.Exists(beatmapFilePath))
        {
            widescreen = readWidescreenFlag(beatmapFilePath);
            objects.AddRange(parse(beatmapFilePath, fromOsb: false, cancellationToken));
        }

        // Fail-layer sprites only show while failing; a music player is always passing.
        objects.RemoveAll(static item => item.layer == Layer.Fail || item is StoryboardBackgroundObject);
        if (objects.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < objects.Count; i++)
        {
            objects[i].Z = i;
        }

        var textures = decodeTextures(objects, files, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new StoryboardSession(objects, textures, widescreen);
    }

    private List<StoryboardObject> parse(string path, bool fromOsb, CancellationToken cancellationToken)
    {
        var result = new List<StoryboardObject>();
        try
        {
            using var reader = new OsuFileReader(path);
            var variables = new VariableCollection();
            foreach (var variable in new VariableReader(reader).EnumValues())
            {
                variables.Add(variable);
            }

            foreach (var storyboardObject in new StoryboardReader(new EventReader(reader, variables)).EnumValues())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (storyboardObject is null)
                {
                    continue;
                }

                storyboardObject.FromOsbFile = fromOsb;
                storyboardObject.CalculateAndApplyBaseFrameTime();
                result.Add(storyboardObject);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or NullReferenceException or OverflowException)
        {
            logger.LogWarning(exception, "Could not parse storyboard file {Path}.", path);
        }

        return result;
    }

    private Dictionary<string, SKImage?> decodeTextures(IEnumerable<StoryboardObject> objects, IBeatmapFileResolver files, CancellationToken cancellationToken)
    {
        var textures = new Dictionary<string, SKImage?>(StringComparer.OrdinalIgnoreCase);
        long budget = max_texture_bytes;
        foreach (var name in objects.SelectMany(imageNames).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget <= 0)
            {
                textures[name] = null;
                continue;
            }

            var image = decode(resolveImage(files, name));
            if (image is not null)
            {
                budget -= (long)image.Width * image.Height * 4;
            }

            textures[name] = image;
        }

        return textures;
    }

    private static IEnumerable<string> imageNames(StoryboardObject storyboardObject)
    {
        if (storyboardObject is StoryboardAnimation animation && animation.FrameCount > 0 && animation.FrameBaseImagePath is not null)
        {
            for (var i = 0; i < animation.FrameCount; i++)
            {
                yield return animation.FrameBaseImagePath + i + animation.FrameFileExtension;
            }
        }
        else if (storyboardObject.ImageFilePath is not null)
        {
            yield return storyboardObject.ImageFilePath;
        }
    }

    private static string? resolveImage(IBeatmapFileResolver files, string name)
    {
        var path = files.Resolve(name);
        if (path is not null)
        {
            return path;
        }

        // Some maps reference images without an extension; osu! tries the common ones.
        if (!Path.HasExtension(name))
        {
            foreach (var extension in image_extensions)
            {
                if (files.Resolve(name + extension) is { } candidate)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private SKImage? decode(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            using var bitmap = SKBitmap.Decode(path);
            return bitmap is null ? null : SKImage.FromBitmap(bitmap);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(exception, "Could not decode storyboard image {Path}.", path);
            return null;
        }
    }

    private static bool readWidescreenFlag(string beatmapPath)
    {
        try
        {
            foreach (var line in File.ReadLines(beatmapPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("WidescreenStoryboard", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.EndsWith('1');
                }

                if (trimmed.StartsWith("[Events]", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("[HitObjects]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }
}
