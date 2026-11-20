using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Social;
using Coworkee.Contracts.Social;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Everything the server opted <see cref="EntityType"/> into with AddCoworkeeSocial: rating and reactions in one row, the tags, then the comments.
/// A block shows only with its view permission and follows permission changes live; the Show* parameters hide blocks, the others go to the blocks.
/// </summary>
public partial class CoworkeeSocial : IDisposable
{
    private SocialFeaturesDto? _features;
    private string? _loaded;
    private bool _rating;
    private bool _reactions;
    private bool _tags;
    private bool _comments;

    [Inject] private ISocialApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    [Parameter] public bool ShowRating { get; set; } = true;

    [Parameter] public bool ShowReactions { get; set; } = true;

    [Parameter] public bool ShowTags { get; set; } = true;

    [Parameter] public bool ShowComments { get; set; } = true;

    /// <summary>The heading above the comments.</summary>
    [Parameter] public bool ShowCommentsTitle { get; set; } = true;

    /// <summary>Rating, reactions and tags without inputs (<see cref="CoworkeeRating.ReadOnly"/> and the like).</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <inheritdoc cref="CoworkeeRating.Size"/>
    [Parameter] public Size RatingSize { get; set; } = Size.Medium;

    /// <inheritdoc cref="CoworkeeReactions.Emojis"/>
    [Parameter] public IReadOnlyList<string> Emojis { get; set; } = SocialReactions.Defaults;

    /// <inheritdoc cref="CoworkeeComments.NewStatus"/>
    [Parameter] public CommentStatus NewStatus { get; set; }

    /// <inheritdoc cref="CoworkeeTags.Changed"/>
    [Parameter] public EventCallback<IReadOnlyList<string>> TagsChanged { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    protected override void OnInitialized() => Permissions.Changed += OnPermissionsChanged;

    protected override async Task OnParametersSetAsync()
    {
        // ponytail: one small request per instance and entity type; cache per type if many instances show at once
        if (_loaded != EntityType)
        {
            _loaded = EntityType;
            _features = null;
            await Snackbar.RunAsync(async () => _features = await Api.GetFeaturesAsync(EntityType));
        }

        await ApplyAsync();
    }

    private async Task ApplyAsync()
    {
        var features = _features ?? new SocialFeaturesDto(false, false, false, false);
        _rating = ShowRating && features.Ratings && await Permissions.HasAsync(SocialPermissions.Ratings.View);
        _reactions = ShowReactions && features.Reactions && await Permissions.HasAsync(SocialPermissions.Reactions.View);
        _tags = ShowTags && features.Tags && await Permissions.HasAsync(SocialPermissions.Tags.View);
        _comments = ShowComments && features.Comments && await Permissions.HasAsync(SocialPermissions.Comments.View);
    }

    private void OnPermissionsChanged() => _ = InvokeAsync(async () =>
    {
        await ApplyAsync();
        StateHasChanged();
    });

    public void Dispose() => Permissions.Changed -= OnPermissionsChanged;
}
