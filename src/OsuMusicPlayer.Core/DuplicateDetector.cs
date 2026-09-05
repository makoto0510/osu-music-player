using System.Text;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core;

public sealed class DuplicateDetector : IDuplicateDetector
{
    public bool AreDuplicates(UnifiedBeatmapSet left, UnifiedBeatmapSet right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.OnlineId is > 0 && right.OnlineId is > 0)
        {
            return left.OnlineId == right.OnlineId;
        }

        var leftKey = CreateMetadataKey(left);
        return leftKey is not null && string.Equals(leftKey, CreateMetadataKey(right), StringComparison.Ordinal);
    }

    public string? CreateMetadataKey(UnifiedBeatmapSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var artist = normalize(set.Artist);
        var title = normalize(set.Title);
        if (artist.Length == 0 || title.Length == 0)
        {
            // Work-in-progress maps often have no title or artist yet. Matching them by
            // creator alone would fold unrelated tracks into one entry.
            return null;
        }

        return string.Join('\u001f', artist, title, normalize(set.Creator));
    }

    private static string normalize(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
