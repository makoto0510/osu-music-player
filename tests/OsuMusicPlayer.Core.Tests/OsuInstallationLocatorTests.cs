using System.Runtime.InteropServices;
using FluentAssertions;

namespace OsuMusicPlayer.Core.Tests;

public sealed class OsuInstallationLocatorTests
{
    [Fact]
    public void ValidateManualPath_AcceptsValidLazerInstallation()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("client.realm");
        directory.CreateDirectory("files");

        var result = new OsuInstallationLocator().ValidateManualPath(directory.Path, OsuInstallationKind.Lazer);

        result.Should().Be(new OsuInstallation(OsuInstallationKind.Lazer, System.IO.Path.GetFullPath(directory.Path)));
    }

    [Fact]
    public void ValidateManualPath_RejectsMissingSentinel()
    {
        using var directory = new TestDirectory();
        directory.CreateDirectory("files");

        new OsuInstallationLocator().ValidateManualPath(directory.Path, OsuInstallationKind.Lazer).Should().BeNull();
    }

    [Fact]
    public void ValidateManualPath_RejectsStableOutsideWindows()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TestDirectory();
        directory.CreateFile("osu!.db");
        directory.CreateDirectory("Songs");

        new OsuInstallationLocator().ValidateManualPath(directory.Path, OsuInstallationKind.Stable).Should().BeNull();
    }
}
