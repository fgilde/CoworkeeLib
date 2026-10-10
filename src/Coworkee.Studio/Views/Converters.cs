using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Coworkee.Studio.Views;

public static class Converters
{
    /// <summary>Resource key of an icon (Icon.Rocket) to its geometry.</summary>
    public static IValueConverter Icon { get; } = new FuncValueConverter<string?, Geometry?>(key =>
        key is not null && Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null);

    public static IValueConverter Brush { get; } = new FuncValueConverter<Color, IBrush>(color => new SolidColorBrush(color));

    public static IValueConverter Hex { get; } = new FuncValueConverter<Color, string>(color => $"#{color.R:x2}{color.G:x2}{color.B:x2}".ToUpper(CultureInfo.InvariantCulture));

    public static IValueConverter FileName { get; } = new FuncValueConverter<string?, string?>(path => path is null ? null : Path.GetFileName(path));
}
