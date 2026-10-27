using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The user's choice per notification type, grouped by category: who gets it and whether in the app or by e-mail; every change is saved at once.</summary>
public partial class NotificationPreferencesEditor
{
    private readonly TableGroupDefinition<NotificationPreferenceDto> _byCategory = new() { Selector = p => p.Category ?? string.Empty };
    private List<NotificationPreferenceDto>? _preferences;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        if (!await Snackbar.RunAsync(async () => _preferences = [.. await Api.GetNotificationPreferencesAsync()]))
        {
            _preferences = [];
        }
    }

    private async Task SaveAsync(NotificationPreferenceDto preference, NotificationScope scope, bool inApp, bool email)
    {
        NotificationPreferenceDto? saved = null;
        if (await Snackbar.RunAsync(async () => saved = await Api.SaveNotificationPreferenceAsync(preference.Type, new SaveNotificationPreferenceRequest(scope, inApp, email))))
        {
            _preferences![_preferences.IndexOf(preference)] = saved!;
        }
    }
}
