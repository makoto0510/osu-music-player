using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OsuMusicPlayer.App.Services;

public interface IFileSaver
{
    /// <summary>Asks where to save a file and returns the chosen local path, or <see langword="null"/> when cancelled.</summary>
    Task<string?> PickSavePathAsync(string title, string suggestedFileName, CancellationToken cancellationToken = default);
}

public sealed class AvaloniaFileSaver : IFileSaver
{
    public Task<string?> PickSavePathAsync(string title, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return pickAsync(title, suggestedFileName, cancellationToken);
        }

        return Dispatcher.UIThread.InvokeAsync(() => pickAsync(title, suggestedFileName, cancellationToken), DispatcherPriority.Default, cancellationToken).GetTask().Unwrap();
    }

    private static async Task<string?> pickAsync(string title, string suggestedFileName, CancellationToken cancellationToken)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var provider = window is null ? null : TopLevel.GetTopLevel(window)?.StorageProvider;
        if (provider is null || !provider.CanSave)
        {
            return null;
        }

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "db",
            FileTypeChoices = [new FilePickerFileType("osu! collection database") { Patterns = ["*.db"] }],
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }
}
