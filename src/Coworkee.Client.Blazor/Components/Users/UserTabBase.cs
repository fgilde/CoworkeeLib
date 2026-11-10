using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// A part of the admin user page that stands alone (<see cref="UserId"/> is all it needs), so the users list shows the same component in a dialog.
/// Loads when the user changes and again when someone changes roles, groups or grants of the organisation.
/// Lists that put what the user has on top sort in <see cref="Arrange"/>: never right after a click, so a row does not move away under the mouse.
/// </summary>
public abstract class UserTabBase : CoworkeeComponentBase, IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);
    // how long after the last click a realtime reload may sort the list again; the reload of an own click comes well within it
    private static readonly TimeSpan Calm = TimeSpan.FromSeconds(3);
    private Guid _loaded;
    private long _clickedAt = long.MinValue / 2;
    private CancellationTokenSource? _pending;

    [Parameter, EditorRequired] public Guid UserId { get; set; }

    [Inject] protected ICoworkeeApi Api { get; set; } = null!;

    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    [Inject] protected CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    protected bool Loaded { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        if (UserId == _loaded)
        {
            return;
        }

        _loaded = UserId;
        await PrepareAsync();
        Loaded = await Snackbar.RunAsync(LoadAsync);
        Arrange();
    }

    protected async Task<bool> MayAsync(string permission) =>
        (await Authorization.AuthorizeAsync((await AuthenticationState).User, PermissionPolicy.For(permission))).Succeeded;

    /// <summary>Once per user before the first load, e.g. the permission checks.</summary>
    protected virtual Task PrepareAsync() => Task.CompletedTask;

    /// <summary>Reads the saved state of the user; toggles that are not saved yet stay shown on top of it.</summary>
    protected abstract Task LoadAsync();

    /// <summary>
    /// Sorts the list from what it shows now (e.g. the user's roles first). Runs after the first load, when the filter changes and after a realtime reload
    /// once the administrator stopped clicking (<see cref="Clicked"/>); in between ticked and unticked rows stay where they are.
    /// </summary>
    protected virtual void Arrange()
    {
    }

    /// <summary>Call on every toggle: holds the order (<see cref="Arrange"/>) while the administrator is clicking.</summary>
    protected void Clicked() => _clickedAt = Environment.TickCount64;

    // the topic of every role, group, membership and grant change of the organisation (IdentityRealtimeTopics); a save touches several rows and an
    // administrator ticking ten groups sends ten: one load for the burst
    protected async Task ReloadAsync()
    {
        _pending?.Cancel();
        var pending = _pending = new CancellationTokenSource();
        try
        {
            await Task.Delay(Settle, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Loaded = await Snackbar.RunAsync(LoadAsync) || Loaded;
        if (Environment.TickCount64 - _clickedAt >= Calm.TotalMilliseconds)
        {
            Arrange();
        }

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        _pending?.Cancel();
        GC.SuppressFinalize(this);
    }

    /// <summary>The snackbar of a save that failed; the item already shows its saved state again.</summary>
    protected async Task SettledAsync(string? failure, string item, Action saved)
    {
        if (failure is null)
        {
            saved();
        }
        else
        {
            Snackbar.Add(L["{0} was not saved: {1}", item, failure], Severity.Error);
        }

        await InvokeAsync(StateHasChanged);
    }
}
