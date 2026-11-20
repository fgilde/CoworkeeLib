using System.Net;
using System.Text.Json;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Shows toasts of the server (<c>INotifier.ToastAsync</c>, <c>Delivery.Toast</c>) as snackbars, translated, with the link as action. It follows the topics an
/// event target reaches: the user's own, the organisation's, the global one and the session's. <see cref="CoworkeeAppShell"/> places it.
/// </summary>
public partial class ToastNotifier : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _subscriptions = [];

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (!ClientUser.From(user).TryGetUserId(out var me))
        {
            return;
        }

        List<string> topics = [RealtimeTopics.User(me), RealtimeTopics.TenantEvents, RealtimeTopics.GlobalEvents];
        if (user!.FindFirst("sid")?.Value is { Length: > 0 } sid)
        {
            topics.Add(RealtimeTopics.Session(sid));
        }

        foreach (var topic in topics)
        {
            _subscriptions.Add(await Realtime.SubscribeAsync(topic, envelope => envelope.Type == ToastPayload.Event ? InvokeAsync(() => Show(envelope)) : Task.CompletedTask));
        }
    }

    private void Show(RealtimeEnvelope envelope)
    {
        if (envelope.Payload.Deserialize<ToastPayload>(JsonSerializerOptions.Web) is not { } toast)
        {
            return;
        }

        var title = WebUtility.HtmlEncode(L.Server(toast.Title, toast.Arguments));
        var body = L.Server(toast.Body, toast.Arguments) is { Length: > 0 } text ? $"<br />{WebUtility.HtmlEncode(text)}" : null;
        Snackbar.Add(new MarkupString($"<b>{title}</b>{body}"), Look(toast.Severity), options =>
        {
            if (toast.Link is { Length: > 0 } link && link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal))
            {
                options.Action = L["Open"];
                options.ActionColor = Color.Inherit;
                options.OnClick = _ =>
                {
                    Nav.NavigateTo(Links.Href(link) ?? link);
                    return Task.CompletedTask;
                };
            }
        });
    }

    private static Severity Look(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => Severity.Success,
        NotificationSeverity.Warning => Severity.Warning,
        NotificationSeverity.Error => Severity.Error,
        NotificationSeverity.Normal => Severity.Normal,
        _ => Severity.Info,
    };

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
