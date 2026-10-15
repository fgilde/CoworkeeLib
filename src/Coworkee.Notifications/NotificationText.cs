using System.Globalization;
using Coworkee.Application.Localization;

namespace Coworkee.Notifications;

internal static class NotificationText
{
    /// <summary>Localizable notifications (with arguments) in the reader's language, the others as written.</summary>
    public static string? In(string? text, IReadOnlyList<string>? arguments, CultureInfo? culture, IEnumerable<ITextTranslator> translators) =>
        text is null || arguments is null || culture is null ? Notification.Format(text, arguments) : Notification.Format(translators.Translate(text, culture), arguments);
}
