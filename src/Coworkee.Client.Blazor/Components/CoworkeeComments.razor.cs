using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Social;
using Coworkee.Contracts.Social;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Threaded comments of one entity registered with AddCoworkeeSocial(s => s.Comments(...)); new comments of others appear live.</summary>
public partial class CoworkeeComments : IAsyncDisposable
{
    private CommentThreadDto? _thread;
    private Guid _me;
    private Guid? _replyTo;
    private Guid? _editing;
    private string? _topic;
    private IAsyncDisposable? _subscription;

    [Inject] private ISocialApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        _ = Guid.TryParse(user?.FindFirst("sub")?.Value, out _me);
    }

    protected override async Task OnParametersSetAsync()
    {
        var topic = SocialTopics.Comments(EntityType, EntityId);
        if (topic == _topic)
        {
            return;
        }

        _topic = topic;
        _thread = null;
        await UnsubscribeAsync();
        _subscription = await Realtime.SubscribeAsync(topic, _ => InvokeAsync(async () =>
        {
            await LoadAsync();
            StateHasChanged();
        }));
        await LoadAsync();
    }

    public ValueTask DisposeAsync() => UnsubscribeAsync();

    private Task<bool> LoadAsync() => Snackbar.RunAsync(async () => _thread = await Api.GetCommentsAsync(EntityType, EntityId));

    private async Task<bool> AddAsync(string text, Guid? parentId)
    {
        var added = await Snackbar.RunAsync(() => Api.AddCommentAsync(EntityType, EntityId, text, parentId));
        if (added)
        {
            _replyTo = null;
            await LoadAsync();
        }

        return added;
    }

    private async Task<bool> EditAsync(CommentDto comment, string text)
    {
        var saved = await Snackbar.RunAsync(() => Api.EditCommentAsync(comment.Id, text));
        if (saved)
        {
            _editing = null;
            await LoadAsync();
        }

        return saved;
    }

    private async Task DeleteAsync(CommentDto comment)
    {
        if (await Snackbar.RunAsync(() => Api.DeleteCommentAsync(comment.Id)))
        {
            await LoadAsync();
        }
    }

    private string Caption(CommentDto comment) =>
        comment.CreatedAt.ToLocalTime().ToString("g") + (comment.EditedAt is null ? string.Empty : " · " + L["edited"]);

    private IEnumerable<(CommentDto Comment, int Depth)> Threaded()
    {
        var comments = _thread?.Comments ?? [];
        var ids = comments.Select(c => c.Id).ToHashSet();
        var replies = comments.Where(c => c.ParentId is { } parent && ids.Contains(parent)).ToLookup(c => c.ParentId);
        return comments.Where(c => c.ParentId is not { } parent || !ids.Contains(parent)).SelectMany(c => Walk(c, 0));

        IEnumerable<(CommentDto, int)> Walk(CommentDto comment, int depth) =>
            replies[comment.Id].SelectMany(reply => Walk(reply, depth + 1)).Prepend((comment, depth));
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
