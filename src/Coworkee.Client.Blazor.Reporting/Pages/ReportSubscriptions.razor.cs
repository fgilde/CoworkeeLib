using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Pages;

/// <summary>The user's report subscriptions; holders of Reports.Subscriptions also see and end those of the organisation.</summary>
public partial class ReportSubscriptions
{
    private IReadOnlyList<ReportSubscriptionDto>? _subscriptions;
    private bool _canSeeAll;
    private bool _all;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        _canSeeAll = await Permissions.HasAsync(ReportPermissions.Subscriptions);
        await LoadAsync();
    }

    private async Task ScopeChanged(bool all)
    {
        _all = all;
        await LoadAsync();
    }

    private Task LoadAsync() => RunAsync(async () => _subscriptions = await Api.GetSubscriptionsAsync(_all));

    private async Task EditAsync(ReportSubscriptionDto subscription)
    {
        if (await ReportSubscriptionDialog.ShowAsync(Dialogs, L["Subscribe by mail"], subscription.ReportId, subscription.Filters ?? [], subscription) is { } request)
        {
            await RunAsync(async () =>
            {
                await Api.UpdateSubscriptionAsync(subscription.Id, request);
                await LoadAsync();
            });
        }
    }

    private Task SendAsync(ReportSubscriptionDto subscription) => RunAsync(async () =>
    {
        var sent = await Api.SendSubscriptionAsync(subscription.Id);
        Snackbar.Add(sent.LastError is { Length: > 0 } error ? L["Last mail failed: {0}", error] : L["The mail is on its way."], sent.LastError is null ? Severity.Success : Severity.Warning);
        await LoadAsync();
    });

    private async Task DeleteAsync(ReportSubscriptionDto subscription)
    {
        if (await Dialogs.ConfirmAsync(L["End the subscription"], L["{0} will no longer come by mail.", L.Stored(subscription.ReportName) ?? subscription.ReportName], L["End the subscription"], L["Cancel"],
                Icons.Material.Outlined.Delete))
        {
            await RunAsync(async () =>
            {
                await Api.DeleteSubscriptionAsync(subscription.Id);
                await LoadAsync();
            });
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : L["That did not work: {0}", exception.Message], Severity.Error);
        }
    }
}
