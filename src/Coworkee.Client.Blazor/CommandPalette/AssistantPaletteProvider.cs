using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>"Ask the assistant: ..." below everything else, while the assistant is set up and the user may chat.</summary>
internal sealed class AssistantPaletteProvider : ICommandPaletteProvider
{
    public const string Group = "Assistant";
    private readonly IAssistantApi api;
    private readonly AssistantState state;
    private readonly PermissionStore permissions;
    private readonly CoworkeeLocalizer l;
    private bool? _available;

    // known until the settings or the permissions change (switched on or off, Ai.Chat granted or taken)
    public AssistantPaletteProvider(IAssistantApi api, AssistantState state, PermissionStore permissions, CoworkeeLocalizer l, Theming.ThemeService themes)
    {
        this.api = api;
        this.state = state;
        this.permissions = permissions;
        this.l = l;
        themes.Changed += () => _available = null;
        permissions.Changed += () => _available = null;
    }

    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        if (text.Length == 0 || !await AvailableAsync(cancellationToken))
        {
            return [];
        }

        return
        [
            new CommandPaletteItem(Group, l["Ask the assistant: {0}", text])
            {
                Icon = MudBlazor.Icons.Material.Outlined.AutoAwesome,
                Matched = true,
                Order = 1000,
                Run = () =>
                {
                    state.Ask(text);
                    return Task.CompletedTask;
                },
            },
        ];
    }

    private async Task<bool> AvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return _available ??= await permissions.HasAsync(AiPermissions.Chat) && (await api.GetStatusAsync(cancellationToken)).Available;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return false;
        }
    }
}
