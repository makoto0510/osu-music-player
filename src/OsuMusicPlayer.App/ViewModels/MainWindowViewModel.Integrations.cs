using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Status surface for the external integrations (server, Discord, osu! API).</summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string serverStatusText = "Server is off.";

    [ObservableProperty]
    private string richPresenceStatusText = "Rich Presence is off.";

    [ObservableProperty]
    private string onlineMetadataStatusText = string.Empty;

    [RelayCommand]
    private Task FetchOnlineMetadataAsync() => fetchOnlineMetadataAsync();
}
