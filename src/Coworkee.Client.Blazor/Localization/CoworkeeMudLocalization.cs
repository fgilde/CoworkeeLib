using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>MudBlazor's own texts (pager, filters, dialogs) from the same translations, keyed like "MudDataGridPager_RowsPerPage".</summary>
internal sealed class CoworkeeMudLocalization(ILoggerFactory loggers, CoworkeeLocalizer texts, MudLocalizer? localizer = null)
    : DefaultLocalizationInterceptor(loggers, localizer)
{
    public override LocalizedString Handle(string key, params object[] arguments) =>
        texts.Find(key) is { } text
            ? new LocalizedString(key, arguments.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, arguments))
            : base.Handle(key, arguments);
}
