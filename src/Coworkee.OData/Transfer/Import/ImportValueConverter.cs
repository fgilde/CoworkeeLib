using System.Globalization;
using Coworkee.Contracts.Data;

namespace Coworkee.OData.Transfer.Import;

/// <summary>
/// Reads a cell into the field's type. Dates try ISO first, then the culture; numbers of typed files (Excel, JSON) are invariant;
/// enums match name, number, label or its translation; booleans know yes/no words of the culture.
/// </summary>
public sealed class ImportValueConverter(CultureInfo culture, bool typed, Func<string, string> translate)
{
    private static readonly string[] True = ["true", "yes", "y", "1", "x", "on"];
    private static readonly string[] False = ["false", "no", "n", "0", "off", "-"];

    public ImportValueConverter(CultureInfo culture, bool typed)
        : this(culture, typed, text => text)
    {
    }

    /// <summary>The value, or an error text (English, a localization key with {0} the cell) when the cell does not fit.</summary>
    public (object? Value, string? Error) Convert(string? text, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (string.IsNullOrWhiteSpace(text))
        {
            return (target == typeof(string) ? text : null, null);
        }

        if (target == typeof(string))
        {
            return (text, null);
        }

        var value = text.Trim();
        object? result = target switch
        {
            _ when target.IsEnum => Enum(value, target),
            _ when target == typeof(bool) => Bool(value),
            _ when target == typeof(Guid) => Guid.TryParse(value, out var guid) ? guid : null,
            _ when target == typeof(DateTime) => Date(value) is { } date ? date.DateTime : null,
            _ when target == typeof(DateTimeOffset) => Date(value),
            _ when target == typeof(DateOnly) => Date(value) is { } day ? DateOnly.FromDateTime(day.DateTime) : null,
            _ when target == typeof(TimeOnly) => TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var time) || TimeOnly.TryParse(value, culture, out time) ? time : null,
            _ when target == typeof(TimeSpan) => TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span) || TimeSpan.TryParse(value, culture, out span) ? span : null,
            _ => Number(value, target),
        };
        return result is null ? (null, "\"{0}\" is no valid value.") : (result, null);
    }

    private object? Number(string value, Type target)
    {
        (CultureInfo Format, NumberStyles Styles)[] attempts = typed
            ? [(CultureInfo.InvariantCulture, NumberStyles.Float), (culture, NumberStyles.Number)]
            : [(culture, NumberStyles.Number | NumberStyles.AllowExponent)];
        foreach (var (format, styles) in attempts)
        {
            if (target == typeof(double) || target == typeof(float))
            {
                if (double.TryParse(value, styles, format, out var real))
                {
                    return System.Convert.ChangeType(real, target, CultureInfo.InvariantCulture);
                }
            }
            else if (decimal.TryParse(value, styles, format, out var number))
            {
                return target != typeof(decimal) && decimal.Truncate(number) != number ? null : Change(number, target);
            }
        }

        return null;
    }

    private static object? Change(decimal number, Type target)
    {
        try
        {
            return System.Convert.ChangeType(number, target, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private DateTimeOffset? Date(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var iso) && value.Length >= 10 && value[4] == '-' ? iso
        : DateTimeOffset.TryParse(value, culture, DateTimeStyles.AssumeUniversal, out var local) ? local
        : null;

    private object? Bool(string value)
    {
        bool Is(string[] words, string english) =>
            words.Contains(value, StringComparer.OrdinalIgnoreCase) || string.Equals(translate(english), value, StringComparison.CurrentCultureIgnoreCase);

        return Is(True, "Yes") ? true : Is(False, "No") ? false : null;
    }

    private object? Enum(string value, Type target)
    {
        if (System.Enum.GetNames(target).FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase)) is { } name)
        {
            return System.Enum.Parse(target, name);
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && System.Enum.IsDefined(target, System.Convert.ChangeType(number, System.Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture)))
        {
            return System.Enum.ToObject(target, number);
        }

        return System.Enum.GetNames(target)
            .Where(n => string.Equals(EnumLabels.Of(target, n), value, StringComparison.CurrentCultureIgnoreCase)
                        || string.Equals(translate(EnumLabels.Of(target, n)), value, StringComparison.CurrentCultureIgnoreCase))
            .Select(n => System.Enum.Parse(target, n))
            .FirstOrDefault();
    }
}
