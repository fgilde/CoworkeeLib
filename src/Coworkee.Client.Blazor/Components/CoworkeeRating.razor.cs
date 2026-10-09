using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Social;
using Coworkee.Contracts.Social;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>1 to 5 stars per user for one entity registered with AddCoworkeeSocial(s => s.Ratings(...)), with the average of everyone.</summary>
public partial class CoworkeeRating
{
    private RatingDto? _rating;

    [Inject] private ISocialApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public Size Size { get; set; } = Size.Medium;

    protected override Task OnParametersSetAsync() => Snackbar.RunAsync(async () => _rating = await Api.GetRatingAsync(EntityType, EntityId));

    private Task RateAsync(int stars) =>
        stars is < 1 or > 5 ? ClearAsync() : Snackbar.RunAsync(async () => _rating = await Api.RateAsync(EntityType, EntityId, stars));

    private Task ClearAsync() => Snackbar.RunAsync(async () => _rating = await Api.ClearRatingAsync(EntityType, EntityId));
}
