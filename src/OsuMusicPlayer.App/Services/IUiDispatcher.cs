namespace OsuMusicPlayer.App.Services;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}
