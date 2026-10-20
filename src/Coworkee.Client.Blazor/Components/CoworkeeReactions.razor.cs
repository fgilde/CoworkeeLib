using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.People;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Social;
using Coworkee.Contracts.Social;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Emoji reactions of one entity registered with AddCoworkeeSocial(s => s.Reactions(...)): counts, the own ones highlighted, who reacted on hover and a small picker; live.
/// With <see cref="Value"/> the parent loads and follows the reactions (comment threads) and this only shows and toggles them.
/// </summary>
public partial class CoworkeeReactions : IAsyncDisposable
{
    private const int NamesShown = 10;

    private ReactionsDto? _reactions;
    private Guid? _me;
    private string? _topic;
    private IAsyncDisposable? _subscription;
    private readonly Dictionary<string, string> _names = [];

    [Inject] private ISocialApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    /// <summary>The emojis of the picker; <see cref="SocialReactions.Defaults"/> unless set.</summary>
    [Parameter] public IReadOnlyList<string> Emojis { get; set; } = SocialReactions.Defaults;

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Reactions the parent already has; nothing is loaded or followed then.</summary>
    [Parameter] public ReactionsDto? Value { get; set; }

    /// <summary>After the user toggled a reaction, with the new state.</summary>
    [Parameter] public EventCallback<ReactionsDto> ValueChanged { get; set; }

    protected override async Task OnInitializedAsync() =>
        _me = ClientUser.From(AuthenticationState is null ? null : (await AuthenticationState).User).UserId;

    protected override async Task OnParametersSetAsync()
    {
        if (Value is not null)
        {
            _reactions = Value;
            _names.Clear();
            return;
        }

        var topic = SocialTopics.Reactions(EntityType, EntityId);
        if (topic == _topic)
        {
            return;
        }

        _topic = topic;
        _reactions = null;
        await UnsubscribeAsync();
        _subscription = await Realtime.SubscribeAsync(topic, _ => InvokeAsync(async () =>
        {
            await LoadAsync();
            StateHasChanged();
        }));
        await LoadAsync();
    }

    public ValueTask DisposeAsync() => UnsubscribeAsync();

    private bool Mine(ReactionDto reaction) => _me is { } me && reaction.UserIds.Contains(me);

    private async Task LoadAsync()
    {
        await Snackbar.RunAsync(async () => _reactions = await Api.GetReactionsAsync(EntityType, EntityId));
        _names.Clear();
    }

    private async Task ToggleAsync(string emoji)
    {
        if (_reactions?.CanReact != true || ReadOnly)
        {
            return;
        }

        ReactionsDto? changed = null;
        if (await Snackbar.RunAsync(async () => changed = await Api.ToggleReactionAsync(EntityType, EntityId, emoji)))
        {
            _reactions = changed;
            _names.Clear();
            await ValueChanged.InvokeAsync(changed!);
        }
    }

    private async Task LoadNamesAsync(ReactionDto reaction)
    {
        if (_names.ContainsKey(reaction.Emoji))
        {
            return;
        }

        var names = new List<string>();
        foreach (var userId in reaction.UserIds.Take(NamesShown))
        {
            names.Add((await Cards.GetAsync(userId))?.Name ?? "?");
        }

        var more = reaction.UserIds.Count - NamesShown;
        _names[reaction.Emoji] = string.Join(", ", names) + (more > 0 ? " " + L["and {0} more", more] : string.Empty);
    }

    private async ValueTask UnsubscribeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }
    }
}
