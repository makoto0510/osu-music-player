using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OsuMusicPlayer.Mobile.App;

public partial class MainView : UserControl
{
    public MainView() => AvaloniaXamlLoader.Load(this);
}
