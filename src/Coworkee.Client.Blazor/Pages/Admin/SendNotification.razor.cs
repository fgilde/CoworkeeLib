using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Sends a notification to a user selection through <c>INotifier.SendAsync</c> on the server (POST notifications/send).</summary>
public partial class SendNotification
{
    /// <summary>The server's generic type (NotificationRequest.DefaultType).</summary>
    private const string DefaultType = "notification";

    private UserSelection _recipients = new();
    private UserSelectionCountDto? _count;
    private IReadOnlyList<NotificationPreferenceDto> _types = [];
    private string? _title;
    private string? _body;
    private string? _arguments;
    private string? _link;
    private NotificationSeverity _severity = NotificationSeverity.Info;
    private bool _stored = true;
    private bool _toast = true;
    private NotificationMail _mail = NotificationMail.Never;
    private string _type = DefaultType;
    private bool _sending;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private bool CanSend => !_sending && !string.IsNullOrWhiteSpace(_title) && !_recipients.IsEmpty && _count is not { Active: 0 } && (_stored || _toast || _mail != NotificationMail.Never);

    protected override Task OnInitializedAsync() => Snackbar.RunAsync(async () => _types = await Api.GetNotificationPreferencesAsync());

    private async Task SendAsync()
    {
        var question = L["Send \"{0}\" to {1} users?", _title!, _count?.Active ?? 0];
        if (!await Dialogs.ConfirmAsync(L["Send notification"], question, L["Send"], L["Cancel"], Icons.Material.Outlined.Send))
        {
            return;
        }

        _sending = true;
        var arguments = (_arguments ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var sent = await Snackbar.RunAsync(() => Api.SendNotificationAsync(new SendNotificationRequest(_recipients, _title!, _body)
        {
            Arguments = arguments.Length == 0 ? null : arguments,
            Link = _link,
            Severity = _severity,
            Stored = _stored,
            Toast = _toast,
            Mail = _mail,
            Type = _type,
        }), L["Notification sent"]);
        _sending = false;
        if (sent)
        {
            (_title, _body, _arguments, _link) = (null, null, null, null);
        }
    }
}
