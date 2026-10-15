using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>Dark mode, themes, languages, the active role and the commands the app registered with AddCommandPaletteCommand.</summary>
internal sealed class ActionsPaletteProvider(
    IEnumerable<CommandPaletteCommand> commands, IServiceProvider services, ICoworkeeApi api, ThemeService themes,
    CoworkeeLocalizer l, PermissionStore permissions, AuthenticationStateProvider authentication) : ICommandPaletteProvider
{
    public const string Group = "Actions";
    private const int Order = 20;
    private IReadOnlyList<ThemeDto>? _themes;
    private MyRolesDto? _roles;

    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken)
    {
        var items = new List<CommandPaletteItem>();
        foreach (var command in commands)
        {
            if (command.Permission is null || await permissions.HasAsync(command.Permission))
            {
                items.Add(Item(l[command.Title], command.Icon, command.Run is { } run ? () => run(services) : null, command.Keywords) with { Href = command.Href });
            }
        }

        var signedIn = (await authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true;
        // ponytail: "system" mode counts as light; the palette does not ask the browser like the layout does
        var dark = themes.Mode == "dark";
        items.Add(Item(l[dark ? "Switch to light mode" : "Switch to dark mode"], dark ? Icons.Material.Outlined.LightMode : Icons.Material.Outlined.DarkMode,
            () => SetModeAsync(dark ? "light" : "dark", signedIn), "dark light mode theme"));
        foreach (var theme in (await ThemesAsync(cancellationToken)).Where(t => t.Id != themes.Current?.Id))
        {
            items.Add(Item(l["Switch theme: {0}", theme.Name], Icons.Material.Outlined.Palette, () => UseThemeAsync(theme, signedIn), "theme colors"));
        }

        foreach (var language in l.Languages.Where(x => x.IsEnabled && !string.Equals(x.Culture, l.Culture, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(Item(l["Switch language: {0}", language.Name], Icons.Material.Outlined.Translate, () => UseLanguageAsync(language.Culture, signedIn), "language " + language.Culture));
        }

        if (signedIn && await RolesAsync(cancellationToken) is { Enabled: true, Roles.Count: > 1 } roles)
        {
            items.AddRange(roles.Roles.Where(r => r.Id != roles.ActiveRoleId)
                .Select(r => Item(l["Act as: {0}", r.Name], Icons.Material.Outlined.Shield, () => UseRoleAsync(r.Id), "switch active role")));
            if (roles.ActiveRoleId is not null)
            {
                items.Add(Item(l["Act as: {0}", l["All roles"]], Icons.Material.Outlined.Shield, () => UseRoleAsync(null), "switch active role"));
            }
        }

        return items;
    }

    private static CommandPaletteItem Item(string title, string icon, Func<Task>? run, string? keywords) =>
        new(Group, title) { Icon = icon, Run = run, Keywords = keywords, Order = Order };

    private async Task<IReadOnlyList<ThemeDto>> ThemesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return _themes ??= await api.GetThemesAsync(cancellationToken) ?? [];
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return _themes = [];
        }
    }

    private async Task<MyRolesDto?> RolesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return _roles ??= await api.GetMyRolesAsync(cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return null;
        }
    }

    private Task SetModeAsync(string mode, bool signedIn)
    {
        themes.SetMode(mode);
        return SaveAsync(signedIn, ThemeSettings.Mode, mode);
    }

    private Task UseThemeAsync(ThemeDto theme, bool signedIn)
    {
        themes.Apply(theme);
        return SaveAsync(signedIn, ThemeSettings.ThemeId, theme.Id.ToString());
    }

    private async Task UseLanguageAsync(string culture, bool signedIn)
    {
        await l.UseAsync(culture);
        await SaveAsync(signedIn, LocalizationSettings.Culture, culture);
    }

    private async Task UseRoleAsync(Guid? roleId)
    {
        await api.SetActiveRoleAsync(roleId);
        _roles = null;
        permissions.Reset();
    }

    private async Task SaveAsync(bool signedIn, string key, string value)
    {
        if (!signedIn)
        {
            return;
        }

        try
        {
            await api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value });
        }
        catch (ApiException)
        {
        }
    }
}
