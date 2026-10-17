using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Chat;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>What <see cref="ChatPeopleDialog"/> closes with: the picked people and, for a new group, its name.</summary>
public sealed record ChatPeoplePick(IReadOnlyList<Guid> UserIds, string? Name = null);

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

public static class ChatPeopleDialogs
{
    public static async Task<ChatPeoplePick?> PickPeopleAsync(
        this IDialogService dialogs, string title, bool multiple = false, bool askName = false, IReadOnlyCollection<Guid>? exclude = null)
    {
        var reference = await dialogs.ShowExAsync<ChatPeopleDialog>(title, new DialogParameters<ChatPeopleDialog>
        {
            { d => d.Multiple, multiple },
            { d => d.AskName, askName },
            { d => d.Exclude, exclude ?? [] },
        }, Data.EditDialogExtensions.Small());
        return await reference.Result is { Canceled: false, Data: ChatPeoplePick pick } ? pick : null;
    }
}
