using Coworkee.Client.Blazor.People;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The picture of a user, or the initials; updates when the user changes it.</summary>
public partial class UserAvatar : IDisposable
{
    private UserCardDto? _card;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Parameter, EditorRequired] public Guid UserId { get; set; }

    [Parameter] public Size Size { get; set; } = Size.Medium;

    /// <summary>A size in pixels beyond the MudBlazor sizes, e.g. 160 on the profile page.</summary>
    [Parameter] public int? Pixels { get; set; }

    [Parameter] public bool ShowTooltip { get; set; }

    private string? StyleText => Pixels is { } px ? $"width:{px}px;height:{px}px;font-size:{px / 2.5}px" : null;

    private string Initials => _card is null
        ? "?"
        : string.Concat(_card.Name.Split(' ', '@', '.').Where(p => p.Length > 0).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    protected override void OnInitialized() => Cards.Changed += Reload;

    protected override async Task OnParametersSetAsync()
    {
        if (_card?.Id != UserId)
        {
            _card = await Cards.GetAsync(UserId);
        }
    }

    public void Dispose() => Cards.Changed -= Reload;

    private void Reload(Guid userId)
    {
        if (userId == UserId)
        {
            _ = InvokeAsync(async () =>
            {
                _card = await Cards.GetAsync(UserId);
                StateHasChanged();
            });
        }
    }
}
