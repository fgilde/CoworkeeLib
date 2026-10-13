using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Clients
{
    private static readonly string[] StandardScopes = ["openid", "profile", "email", "roles", "offline_access", "phone", "address"];
    private static readonly string[] Required = ["openid"];

    private IReadOnlyList<ClientDto> _clients = [];
    private IReadOnlyList<RoleDto> _roles = [];
    private IReadOnlyList<string> _permissions = [];
    private IReadOnlyList<string> _scopeNames = StandardScopes;
    private bool _busy;

    [Inject] private IClientsApi Api { get; set; } = null!;

    [Inject] private ICoworkeeApi Identity { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => RunAsync(async () =>
    {
        _roles = await Identity.GetRolesAsync();
        _permissions = [.. (await Identity.GetPermissionDefinitionsAsync()).SelectMany(g => g.Permissions).Select(p => p.Name)];
    });

    [GeneratedRegex("^[A-Za-z0-9._:-]+$")]
    private static partial Regex ScopePattern();

    private static bool IsService(ClientDto client) => client.GrantTypes.Contains(ClientGrantTypes.ClientCredentials);

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private Task SetRightsAsync(ClientDto client, IReadOnlyList<Guid> roles, IReadOnlyList<string> permissions) =>
        RunAsync(() => Api.UpdateClientAsync(client.Id, ClientForm.From(client).ToRequest() with { Roles = roles, Permissions = permissions }));

    private async Task CreateAsync()
    {
        ClientSecretDto? created = null;
        await LoadScopeNamesAsync();
        if (await Dialogs.ShowEditAsync(L["New application"], new ClientForm(), async form => created = await Api.CreateClientAsync(form.ToRequest()), Meta))
        {
            await ShowSecretAsync(created);
            await RunAsync(() => Task.CompletedTask);
        }
    }

    private async Task EditAsync(ClientDto client)
    {
        ClientSecretDto? updated = null;
        await LoadScopeNamesAsync();
        if (await Dialogs.ShowEditAsync(L["Edit application"], ClientForm.From(client), async form => updated = await Api.UpdateClientAsync(client.Id, form.ToRequest()), Meta))
        {
            await ShowSecretAsync(updated);
            await RunAsync(() => Task.CompletedTask);
        }
    }

    private async Task RegenerateAsync(ClientDto client)
    {
        if (await Dialogs.ConfirmAsync(L["New secret"], L["Create a new secret for {0}? The current one stops working.", client.ClientId], L["New secret"], L["Cancel"],
                Icons.Material.Outlined.Key))
        {
            ClientSecretDto? secret = null;
            await RunAsync(async () => secret = await Api.RegenerateSecretAsync(client.Id));
            await ShowSecretAsync(secret);
        }
    }

    private async Task DeleteAsync(ClientDto client)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", client.ClientId], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            await RunAsync(() => Api.DeleteClientAsync(client.Id));
        }
    }

    // the secret is stored as hash only: this is the one chance to copy it
    private async Task ShowSecretAsync(ClientSecretDto? secret)
    {
        if (secret?.ClientSecret is { } value)
        {
            await Dialogs.ShowMessageBoxAsync(L["Client secret"],
                (MarkupString)$"<p>{System.Net.WebUtility.HtmlEncode(L["Copy the secret now, it is not shown again:"])}</p><code data-testid=\"client-secret\">{value}</code>");
        }
    }

    private Task LoadScopeNamesAsync() =>
        Snackbar.RunAsync(async () => _scopeNames = [.. StandardScopes.Union((await Api.GetScopesAsync()).Select(s => s.Name))]);

    private Task<IEnumerable<string>> SuggestScopes(string text, CancellationToken _) =>
        Task.FromResult(_scopeNames.Where(s => s.Contains(text, StringComparison.OrdinalIgnoreCase)));

    private string? AbsoluteAddress(string value) => Uri.TryCreate(value, UriKind.Absolute, out _) ? null : L["Not an absolute address"];

    private string? ScopeName(string value) => ScopePattern().IsMatch(value) ? null : L["Letters, digits and . _ : - only"];

#pragma warning disable BL0005 // MudEx configures the rendered editors through these instances
    private void Meta(ObjectEditMeta<ClientForm> meta)
    {
        meta.Property(f => f.RedirectUris).RenderAsChips(e =>
        {
            e.Label = L["Redirect addresses"];
            e.HelperText = L["Full addresses like https://app.example/callback"];
            e.Validate = AbsoluteAddress;
        });
        meta.Property(f => f.PostLogoutRedirectUris).RenderAsChips(e =>
        {
            e.Label = L["Addresses after sign-out"];
            e.Validate = AbsoluteAddress;
        });
        meta.Property(f => f.Scopes).RenderAsChips(e =>
        {
            e.Label = L["Scopes"];
            e.HelperText = L["Pick a suggestion or type your own"];
            e.Suggestions = SuggestScopes;
            e.Validate = ScopeName;
            e.Fixed = Required;
        });
        meta.Property(f => f.ClientUri).WithAdditionalAttribute(nameof(MudTextField<string>.HelperText), L["The app launcher of the sign-in server links here"]);
        meta.Property(f => f.Description).WithAdditionalAttribute(nameof(MudTextField<string>.Lines), 2);
    }
#pragma warning restore BL0005

    private async Task RunAsync(Func<Task> action, string? success = null)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action, success);
            await Snackbar.RunAsync(async () => _clients = await Api.GetClientsAsync());
        }
        finally
        {
            _busy = false;
        }
    }

    public enum ClientKind
    {
        Public,
        Confidential,
    }

    public enum ConsentKind
    {
        Implicit,
        Explicit,
    }

    private sealed class ClientForm
    {
        public string ClientId { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public ClientKind Type { get; set; }

        public ConsentKind Consent { get; set; }

        [Display(Name = "Redirect addresses")]
        public IReadOnlyList<string> RedirectUris { get; set; } = [];

        [Display(Name = "Addresses after sign-out")]
        public IReadOnlyList<string> PostLogoutRedirectUris { get; set; } = [];

        public IReadOnlyList<string> Scopes { get; set; } = ["openid", "profile", "email", "roles", "offline_access"];

        [Display(Name = "Allow refresh tokens")]
        public bool RefreshTokens { get; set; } = true;

        [Display(Name = "Service client")]
        public bool ServiceClient { get; set; }

        [Display(Name = "Home address")]
        public string? ClientUri { get; set; }

        [Display(Name = "Logo address")]
        public string? LogoUrl { get; set; }

        public string? Description { get; set; }

        [Display(Name = "Show in the app launcher")]
        public bool ShowInLauncher { get; set; } = true;

        private IReadOnlyList<Guid> _roles = [];

        private IReadOnlyList<string> _permissions = [];

        public static ClientForm From(ClientDto client) => new()
        {
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            Type = client.ClientType == "confidential" ? ClientKind.Confidential : ClientKind.Public,
            Consent = client.ConsentType == "explicit" ? ConsentKind.Explicit : ConsentKind.Implicit,
            RedirectUris = client.RedirectUris,
            PostLogoutRedirectUris = client.PostLogoutRedirectUris,
            Scopes = [.. client.Scopes.Prepend("openid").Distinct()],
            RefreshTokens = client.GrantTypes.Contains(ClientGrantTypes.RefreshToken),
            ServiceClient = IsService(client),
            ClientUri = client.ClientUri,
            LogoUrl = client.LogoUrl,
            Description = client.Description,
            ShowInLauncher = client.ShowInLauncher,
            _roles = client.Roles,
            _permissions = client.Permissions,
        };

        public ClientRequest ToRequest() => new(
            ClientId,
            DisplayName,
            Type.ToString().ToLowerInvariant(),
            Consent.ToString().ToLowerInvariant(),
            RedirectUris,
            PostLogoutRedirectUris,
            GrantTypes(),
            Scopes,
            _roles,
            _permissions,
            ClientUri,
            LogoUrl,
            Description,
            ShowInLauncher);

        // a service client without redirect addresses signs nobody in
        private List<string> GrantTypes()
        {
            var grants = new List<string>();
            if (!ServiceClient || RedirectUris.Count > 0)
            {
                grants.Add(ClientGrantTypes.AuthorizationCode);
                if (RefreshTokens)
                {
                    grants.Add(ClientGrantTypes.RefreshToken);
                }
            }

            if (ServiceClient)
            {
                grants.Add(ClientGrantTypes.ClientCredentials);
            }

            return grants;
        }
    }
}
