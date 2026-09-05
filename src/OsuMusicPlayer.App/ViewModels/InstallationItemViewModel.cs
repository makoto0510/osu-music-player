using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.Core;

namespace OsuMusicPlayer.App.ViewModels;

public sealed partial class InstallationItemViewModel : ObservableObject
{
    private readonly Func<InstallationItemViewModel, Task>? remove;

    public InstallationItemViewModel(OsuInstallation installation, bool isManual, Func<InstallationItemViewModel, Task>? remove = null)
    {
        Installation = installation ?? throw new ArgumentNullException(nameof(installation));
        IsManual = isManual;
        this.remove = remove;
    }

    public OsuInstallation Installation { get; }
    public bool IsManual { get; }
    public OsuInstallationKind Kind => Installation.Kind;
    public string Path => Installation.RootPath;
    public string KindText => Kind == OsuInstallationKind.Stable ? "osu!stable" : "osu!lazer";
    public string OriginText => IsManual ? "manual" : "auto-detected";

    public bool Matches(OsuInstallation other) =>
        other is not null &&
        other.Kind == Installation.Kind &&
        string.Equals(other.RootPath, Installation.RootPath, StringComparison.OrdinalIgnoreCase);

    [RelayCommand(CanExecute = nameof(IsManual))]
    private Task RemoveAsync() => remove?.Invoke(this) ?? Task.CompletedTask;
}
