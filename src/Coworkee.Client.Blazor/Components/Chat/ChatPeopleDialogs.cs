using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components.Chat;

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
