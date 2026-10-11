using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.Views;

public static class Converters
{
    /// <summary>Resource key of an icon (Icon.Rocket) to its geometry.</summary>
    public static IValueConverter Icon { get; } = new FuncValueConverter<string?, Geometry?>(key =>
        key is not null && Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null);

    public static IValueConverter Brush { get; } = new FuncValueConverter<Color, IBrush>(color => new SolidColorBrush(color));

    public static IValueConverter Hex { get; } = new FuncValueConverter<Color, string>(color => $"#{color.R:x2}{color.G:x2}{color.B:x2}".ToUpper(CultureInfo.InvariantCulture));

    public static IValueConverter Dim { get; } = new FuncValueConverter<bool, double>(on => on ? 1 : 0.45);

    public static IValueConverter Mirror { get; } = new FuncValueConverter<bool, FlowDirection>(right => right ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);

    public static IValueConverter Positive { get; } = new FuncValueConverter<int, bool>(count => count > 0);

    public static IValueConverter Zero { get; } = new FuncValueConverter<int, bool>(count => count == 0);

    /// <summary>A fixed English choice (Default, Yes, No) in the language of the Studio.</summary>
    public static IValueConverter Text { get; } = new FuncValueConverter<string?, string?>(text => text is null ? null : L.Get(text));

    public static IValueConverter FileName { get; } = new FuncValueConverter<string?, string?>(path => path is null ? null : Path.GetFileName(path));
}
