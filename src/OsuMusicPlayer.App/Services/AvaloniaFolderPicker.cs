using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OsuMusicPlayer.App.Services;

public sealed class AvaloniaFolderPicker : IFolderPicker
{
    public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return pickAsync(title, cancellationToken);
        }

        return Dispatcher.UIThread.InvokeAsync(() => pickAsync(title, cancellationToken), DispatcherPriority.Default, cancellationToken).GetTask().Unwrap();
    }

    private static async Task<string?> pickAsync(string title, CancellationToken cancellationToken)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var provider = window is null ? null : TopLevel.GetTopLevel(window)?.StorageProvider;
        if (provider is null || !provider.CanPickFolder)
        {
            return null;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
