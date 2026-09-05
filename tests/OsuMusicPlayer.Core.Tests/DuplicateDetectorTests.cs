using FluentAssertions;

namespace OsuMusicPlayer.Core.Tests;

public sealed class DuplicateDetectorTests
{
    private readonly DuplicateDetector detector = new();

    [Fact]
    public void SamePositiveOnlineId_IsDuplicate()
    {
        detector.AreDuplicates(ModelFactory.Set(123), ModelFactory.Set(123)).Should().BeTrue();
    }

    [Fact]
    public void DifferentPositiveOnlineIds_AreNotDuplicateEvenWhenMetadataMatches()
    {
        detector.AreDuplicates(ModelFactory.Set(123), ModelFactory.Set(456)).Should().BeFalse();
    }

    [Fact]
    public void MissingOnlineId_UsesConservativeNormalizedMetadata()
    {
        var first = ModelFactory.Set(artist: "  Ｔhe   Artist ", title: "A Song", creator: "Mapper");
        var second = ModelFactory.Set(onlineId: 42, artist: "the artist", title: "a song", creator: "mapper");

        detector.AreDuplicates(first, second).Should().BeTrue();
        detector.CreateMetadataKey(first).Should().Be(detector.CreateMetadataKey(second));
    }

    [Fact]
    public void PunctuationIsNotDiscardedDuringNormalization()
    {
        var first = ModelFactory.Set(title: "Song!");
        var second = ModelFactory.Set(title: "Song");

        detector.AreDuplicates(first, second).Should().BeFalse();
    }

    [Fact]
    public void EmptyMetadata_IsNotDuplicate()
    {
        var first = ModelFactory.Set(artist: string.Empty, title: string.Empty, creator: string.Empty);
        var second = ModelFactory.Set(artist: string.Empty, title: string.Empty, creator: string.Empty);

        detector.AreDuplicates(first, second).Should().BeFalse();
        detector.CreateMetadataKey(first).Should().BeNull();
    }

    [Fact]
    public void SameCreatorWithoutTitleOrArtist_IsNotDuplicate()
    {
        var first = ModelFactory.Set(artist: string.Empty, title: string.Empty, creator: "Mapper");
        var second = ModelFactory.Set(artist: string.Empty, title: string.Empty, creator: "Mapper");
        var third = ModelFactory.Set(artist: "Artist", title: string.Empty, creator: "Mapper");

        detector.AreDuplicates(first, second).Should().BeFalse();
        detector.AreDuplicates(first, third).Should().BeFalse();
        detector.CreateMetadataKey(third).Should().BeNull();
    }
}
