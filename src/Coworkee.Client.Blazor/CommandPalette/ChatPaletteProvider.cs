using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Components.Chat;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Chat;
using MudBlazor;

namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>"Chat with…" picks a person and opens the mini chat; <c>@name</c> lists the matching people to chat with right away.</summary>
internal sealed class ChatPaletteProvider(IChatApi api, ChatDock dock, IDialogService dialogs, PermissionStore permissions, CoworkeeLocalizer l) : ICommandPaletteProvider
{
    public const string Group = "Chat";
    private const int MaxPeople = 8;
    private IReadOnlyList<ChatContactDto>? _people;

    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(ChatPermissions.Use))
        {
            return [];
        }

        var text = query.Trim();
        if (!text.StartsWith('@'))
        {
            return
            [
                new CommandPaletteItem(ActionsPaletteProvider.Group, l["Chat with…"])
                {
                    Icon = Icons.Material.Outlined.MapsUgc, Keywords = "chat message write person direct @", Order = 20, Run = PickAsync,
                },
            ];
        }

        var name = text[1..].Trim();
        try
        {
            _people ??= await api.GetContactsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return [];
        }

        return _people.Where(p => name.Length == 0 || p.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase)).Take(MaxPeople)
            .Select(p => new CommandPaletteItem(Group, l["Chat with {0}", p.Name])
            {
                Icon = Icons.Material.Outlined.Chat, Matched = true, Order = 5, Keywords = p.Name, Run = () => dock.OpenWithAsync(p.UserId),
            });
    }

    private async Task PickAsync()
    {
        if (await dialogs.PickPeopleAsync(l["Chat with…"]) is { UserIds: [var userId] })
        {
            await dock.OpenWithAsync(userId);
        }
    }
}
