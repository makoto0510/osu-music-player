using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OsuMusicPlayer.App;

/// <summary>Turns an icon resource key ("IconHeart") into the geometry declared in Themes/Icons.axaml.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static IconKeyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0 || Application.Current is not { } application)
        {
            return null;
        }

        return application.TryFindResource(key, out var resource) ? resource as Geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
