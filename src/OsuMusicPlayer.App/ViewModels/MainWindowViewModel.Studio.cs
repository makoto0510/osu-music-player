using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OsuMusicPlayer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<OsuMusicPlayer.Audio.OsuAudioMod> StudioMods { get; } = Enum.GetValues<OsuMusicPlayer.Audio.OsuAudioMod>();

    public bool HasStudioSearch => !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand]
    private void ClearStudioSearch() => SearchText = string.Empty;

    [ObservableProperty]
    private bool isStudioQueueVisible;

    public bool IsStudioToolsVisible => IsSettingsPanelVisible || IsSourcesPanelVisible
        || IsEqualizerPanelVisible || IsBrowsePanelVisible || IsPlaylistPanelVisible;

    [RelayCommand]
    private void CloseStudioTools()
    {
        IsSettingsPanelVisible = false;
        IsSourcesPanelVisible = false;
        IsEqualizerPanelVisible = false;
        IsBrowsePanelVisible = false;
        IsPlaylistPanelVisible = false;
    }

    [RelayCommand]
    private void OpenStudioTool(string? tool)
    {
        CloseStudioTools();
        switch (tool)
        {
            case "settings": IsSettingsPanelVisible = true; break;
            case "sources": IsSourcesPanelVisible = true; break;
            case "equalizer": IsEqualizerPanelVisible = true; break;
            case "browse": IsBrowsePanelVisible = true; break;
            case "playlists": IsPlaylistPanelVisible = true; break;
        }
    }
}
