using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>The API keys tab of the account page: the user's keys, a new one with its secret shown once, and how to connect MCP clients.</summary>
public partial class ProfileApiKeys
{
    private IReadOnlyList<ApiKeyDto>? _keys;
    private CreatedApiKeyDto? _created;

    [Inject] private IApiKeysApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    // the web host forwards /mcp to the API, so clients use the address of the app
    private string McpUrl => Nav.BaseUri + "mcp";

    private string ServerName => new string([.. Options.AppTitle.ToLowerInvariant().Where(char.IsLetterOrDigit)]) is { Length: > 0 } name ? name : "app";

    private string McpConfig =>
        $$"""
        {
          "mcpServers": {
            "{{ServerName}}": {
              "type": "http",
              "url": "{{McpUrl}}",
              "headers": { "Authorization": "Bearer {{_created?.Secret}}" }
            }
          }
        }
        """;

    private string ClaudeCode => $"claude mcp add --transport http {ServerName} {McpUrl} --header \"Authorization: Bearer {_created?.Secret}\"";

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task LoadAsync() => Snackbar.RunAsync(async () => _keys = await Api.GetMineAsync());

    private async Task CreateAsync()
    {
        var dialog = await Dialogs.ShowAsync<ApiKeyDialog>(L["New API key"], EditDialogExtensions.Small());
        if (await dialog.Result is { Canceled: false, Data: CreatedApiKeyDto created })
        {
            _created = created;
            await LoadAsync();
        }
    }

    private async Task RevokeAsync(ApiKeyDto key)
    {
        if (await Dialogs.ConfirmAsync(L["Revoke API key"], L["Revoke {0}? Clients using it stop working at once.", key.Name], L["Revoke"], L["Cancel"], Icons.Material.Outlined.KeyOff)
            && await Snackbar.RunAsync(() => Api.RevokeAsync(key.Id), L["{0} revoked", key.Name]))
        {
            await LoadAsync();
        }
    }

    private async Task CopyAsync(string text)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
            Snackbar.Add(L["Copied"], Severity.Success);
        }
        catch (JSException)
        {
            // ponytail: no clipboard (insecure origin, denied permission) - the key stays selectable
        }
    }
}
