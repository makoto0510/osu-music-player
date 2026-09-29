using FluentAssertions;
using OsuMusicPlayer.Mobile.App;
using OsuMusicPlayer.Mobile.Core;
using Xunit;

namespace OsuMusicPlayer.Server.Tests;

public sealed class MobileViewModelTests
{
    [Fact]
    public async Task SavedTrack_CanPlayWithoutServerConnection()
    {
        var track = new MobileTrack(Guid.NewGuid(), "Title", "Artist", "Mapper", 180, 120, null, false, "stable", false);
        using var audio = new FakeAudioService(track);
        using var viewModel = new MobileViewModel(audio);
        viewModel.SelectedDownloadedTrack = track;

        await viewModel.PlayDownloadedCommand.ExecuteAsync(null);

        audio.PlayedId.Should().Be(track.Id);
        viewModel.PhoneTitle.Should().Contain("Artist").And.Contain("Title");
    }

    private sealed class FakeAudioService(MobileTrack track) : IMobileAudioService
    {
        public Guid? PlayedId { get; private set; }

        public Task PlayAsync(Uri audioUri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ToggleAsync() => Task.CompletedTask;
        public Task DownloadAsync(MobileTrack value, Uri audioUri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayDownloadedAsync(Guid id, CancellationToken cancellationToken = default)
        {
            PlayedId = id;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MobileTrack>> GetDownloadedTracksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MobileTrack>>([track]);

        public void Dispose() { }
    }
}
