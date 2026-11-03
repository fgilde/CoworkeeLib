using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Chat;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>Picks a person to chat with (one click), or several people for a group or to add to one.</summary>
public partial class ChatPeopleDialog
{
    private readonly HashSet<Guid> _selected = [];
    private IReadOnlyList<ChatContactDto>? _people;
    private string? _filter;
    private string? _name;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private IChatApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Parameter] public bool Multiple { get; set; }

    /// <summary>A new group: asks for its name too.</summary>
    [Parameter] public bool AskName { get; set; }

    [Parameter] public IReadOnlyCollection<Guid> Exclude { get; set; } = [];

    private IEnumerable<ChatContactDto> Shown => (_people ?? [])
        .Where(p => !Exclude.Contains(p.UserId) && (string.IsNullOrWhiteSpace(_filter) || p.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)))
        .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

    protected override Task OnInitializedAsync() => Snackbar.RunAsync(async () => _people = await Api.GetContactsAsync());

    private Task PickAsync(Guid userId)
    {
        if (!Multiple)
        {
            Dialog.Close(DialogResult.Ok(new ChatPeoplePick([userId])));
        }
        else if (!_selected.Add(userId))
        {
            _selected.Remove(userId);
        }

        return Task.CompletedTask;
    }

    private void Done() => Dialog.Close(DialogResult.Ok(new ChatPeoplePick([.. _selected], _name?.Trim())));

    private void Cancel() => Dialog.Cancel();
}
