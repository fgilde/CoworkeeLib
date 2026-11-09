using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Configuration;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>A user's own session limit: shown next to his sessions, edited on the user page and the sessions page.</summary>
internal static class SessionLimits
{
    public static Task<bool> EditAsync(IDialogService dialogs, ISessionsApi api, CoworkeeLocalizer l, Guid userId, string userName, int? current) =>
        dialogs.ShowEditAsync($"{l["Session limit"]}: {userName}", new SessionLimitForm { MaxSessions = current }, form => api.SetLimitAsync(userId, form.MaxSessions),
            meta => meta.Property(f => f.MaxSessions).WithLabel(l["Session limit"])
                .WithAdditionalAttribute(nameof(MudNumericField<int?>.HelperText), l["Empty: the organisation's setting. 0: unlimited."]));

    /// <summary>The limit in words; the organisation's comes from the client settings.</summary>
    public static string Describe(CoworkeeLocalizer l, int? own, IReadOnlyDictionary<string, string?> settings)
    {
        var limit = own ?? (int.TryParse(settings.GetValueOrDefault(AuthSettings.MaxSessionsPerUser), out var organisation) ? organisation : 0);
        var text = limit <= 0 ? l["unlimited"] : limit.ToString(System.Globalization.CultureInfo.CurrentCulture);
        return own is null ? l["{0} (organisation setting)", text] : text;
    }
}

public sealed class SessionLimitForm
{
    [Range(0, 1000)]
    public int? MaxSessions { get; set; }
}
