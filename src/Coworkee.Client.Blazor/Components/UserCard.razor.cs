using Coworkee.Client.Blazor.People;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Picture and name of a user, with an optional second line, e.g. in lists, chats and the app bar.</summary>
public partial class UserCard : IDisposable
{
    private UserCardDto? _card;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Parameter, EditorRequired] public Guid UserId { get; set; }

    [Parameter] public Size Size { get; set; } = Size.Small;

    [Parameter] public RenderFragment? ChildContent { get; set; }

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
