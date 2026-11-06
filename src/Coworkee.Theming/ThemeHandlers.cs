using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Theming;

/// <summary>
/// The themes of an organisation: the built-in ones and its own. Changing or withdrawing a built-in theme stores the organisation's version of it
/// (<see cref="ThemeDefinition.BuiltInId"/>), which is shown under the built-in theme's id, so defaults and the users' choices keep pointing at it.
/// </summary>
internal sealed class ThemeHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IServiceProvider services, IOptions<ThemingOptions> options)
    : IHandler<GetThemes, Result<IReadOnlyList<ThemeDto>>>,
      IHandler<GetCurrentTheme, Result<ThemeDto>>,
      IHandler<GetChoosableTheme, Result<ThemeDto>>,
      IHandler<GetBuiltInThemes, Result<IReadOnlyList<ThemeDto>>>,
      IHandler<CreateTheme, Result<ThemeDto>>,
      IHandler<UpdateTheme, Result<ThemeDto>>,
      IHandler<DeleteTheme, Result>,
      IHandler<ResetTheme, Result>,
      IHandler<SetThemePublished, Result>,
      IHandler<SetDefaultTheme, Result>
{
    private static readonly Error NotFound = Error.NotFound("themes.not_found", "The theme does not exist.");
    private static readonly Error ReadOnly = Error.Forbidden("themes.read_only", "Built-in themes cannot be deleted.");
    private static readonly Error NotBuiltIn = Error.Validation("themes.not_built_in", "Only built-in themes can be reset.");
    private static readonly Error NoTenant = Error.Validation("Tenant", "Themes belong to an organisation.");

    public async Task<Result<IReadOnlyList<ThemeDto>>> HandleAsync(GetThemes query, CancellationToken cancellationToken)
    {
        await ThemeSeeds.EnsureAsync(db, options.Value, cancellationToken);
        var defaultId = (await EffectiveAsync(currentUser.TenantId, cancellationToken))?.Id;
        var all = await permissions.IsGrantedAsync(ThemePermissions.Manage, cancellationToken);
        var versions = await VersionsAsync(currentUser.TenantId, cancellationToken);
        IReadOnlyList<ThemeDto> themes = (await Visible().AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(t => ToDto(t, t.Id == defaultId, versions.GetValueOrDefault(t.Id)))
            .Where(t => all || t.IsPublished || t.Id == defaultId)
            .ToList();
        return Result<IReadOnlyList<ThemeDto>>.Success(themes);
    }

    public async Task<Result<IReadOnlyList<ThemeDto>>> HandleAsync(GetBuiltInThemes query, CancellationToken cancellationToken)
    {
        await ThemeSeeds.EnsureAsync(db, options.Value, cancellationToken);
        IReadOnlyList<ThemeDto> themes = (await db.Set<ThemeDefinition>().AsNoTracking().Where(t => t.TenantId == null)
                .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(t => ToDto(t, t.IsDefault, null))
            .ToList();
        return Result<IReadOnlyList<ThemeDto>>.Success(themes);
    }

    public async Task<Result<ThemeDto>> HandleAsync(GetCurrentTheme query, CancellationToken cancellationToken)
    {
        await ThemeSeeds.EnsureAsync(db, options.Value, cancellationToken);
        var tenantId = currentUser.TenantId ?? await SystemTenantIdAsync(cancellationToken);
        return await EffectiveAsync(tenantId, cancellationToken) is { } theme
            ? ToDto(theme, true, await VersionAsync(tenantId, theme, cancellationToken))
            : NotFound;
    }

    // independent of the caller's permissions, so the answer is the same for everyone in the organisation (the web app caches it per organisation)
    public async Task<Result<ThemeDto>> HandleAsync(GetChoosableTheme query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId is null)
        {
            return NotFound;
        }

        var theme = await Visible().AsNoTracking().SingleOrDefaultAsync(t => t.Id == query.Id, cancellationToken);
        var dto = theme is null ? null : ToDto(theme, false, await VersionAsync(currentUser.TenantId, theme, cancellationToken));
        return dto is { IsPublished: true } ? dto : NotFound;
    }

    public async Task<Result<ThemeDto>> HandleAsync(CreateTheme command, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return NoTenant;
        }

        if (ThemeValidation.Validate(command.Theme) is { } message)
        {
            return Error.Validation("Theme", message);
        }

        // the organisation's versions of built-in themes carry their names
        var name = command.Theme.Name.Trim();
        if (await db.Set<ThemeDefinition>().AnyAsync(t => (t.TenantId == tenantId || t.TenantId == null) && t.Name == name, cancellationToken))
        {
            return Error.Conflict("themes.name_taken", "A theme with this name already exists.");
        }

        var theme = new ThemeDefinition { Name = name, TenantId = tenantId, PaletteLight = "{}", PaletteDark = "{}" };
        Apply(theme, command.Theme);
        db.Add(theme);
        return ToDto(theme, false, null);
    }

    public async Task<Result<ThemeDto>> HandleAsync(UpdateTheme command, CancellationToken cancellationToken)
    {
        var theme = await Visible().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (theme is null)
        {
            return NotFound;
        }

        if (ThemeValidation.Validate(command.Theme) is { } message)
        {
            return Error.Validation("Theme", message);
        }

        if (theme.TenantId is not null)
        {
            Apply(theme, command.Theme);
            return ToDto(theme, false, null);
        }

        // a built-in theme keeps its name: the organisation changes its look
        if (await VersionForAsync(theme, cancellationToken) is not { } version)
        {
            return NoTenant;
        }

        Apply(version, command.Theme with { Name = theme.Name });
        version.IsCustomized = true;
        return ToDto(theme, false, version);
    }

    public async Task<Result> HandleAsync(DeleteTheme command, CancellationToken cancellationToken)
    {
        var theme = await Visible().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (theme is null)
        {
            return NotFound;
        }

        if (theme.TenantId is null)
        {
            return ReadOnly;
        }

        db.Remove(theme);
        return Result.Success();
    }

    // the version stays (with a new revision) instead of being removed: a revision that came back would find an outdated logo in the browser's cache
    public async Task<Result> HandleAsync(ResetTheme command, CancellationToken cancellationToken)
    {
        var theme = await Visible().AsNoTracking().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (theme is null)
        {
            return NotFound;
        }

        if (theme.TenantId is not null)
        {
            return NotBuiltIn;
        }

        if (await StoredVersionAsync(theme, cancellationToken) is { IsCustomized: true } version)
        {
            Copy(theme, version);
            version.IsCustomized = false;
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(SetThemePublished command, CancellationToken cancellationToken)
    {
        var theme = await Visible().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (theme is null)
        {
            return NotFound;
        }

        if (theme.TenantId is not null)
        {
            theme.IsPublished = command.IsPublished;
            return Result.Success();
        }

        // a built-in theme nobody changed is published without a version of its own
        var stored = await StoredVersionAsync(theme, cancellationToken);
        if (stored is null && command.IsPublished)
        {
            return Result.Success();
        }

        if ((stored ?? await VersionForAsync(theme, cancellationToken)) is not { } version)
        {
            return NoTenant;
        }

        version.IsPublished = command.IsPublished;
        return Result.Success();
    }

    public async Task<Result> HandleAsync(SetDefaultTheme command, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || !await Visible().AnyAsync(t => t.Id == command.Id, cancellationToken))
        {
            return NotFound;
        }

        if (await db.Set<TenantTheme>().SingleOrDefaultAsync(m => m.TenantId == tenantId, cancellationToken) is { } mapping)
        {
            mapping.ThemeId = command.Id;
        }
        else
        {
            db.Add(new TenantTheme { TenantId = tenantId, ThemeId = command.Id });
        }

        return Result.Success();
    }

    // the built-in themes and the organisation's own; its versions of built-in themes show through the built-in ones
    private IQueryable<ThemeDefinition> Visible()
    {
        var tenantId = currentUser.TenantId;
        return db.Set<ThemeDefinition>().Where(t => t.BuiltInId == null && (t.TenantId == null || t.TenantId == tenantId));
    }

    private async Task<Dictionary<Guid, ThemeDefinition>> VersionsAsync(Guid? tenantId, CancellationToken cancellationToken) =>
        tenantId is null
            ? []
            : await db.Set<ThemeDefinition>().AsNoTracking().Where(t => t.TenantId == tenantId && t.BuiltInId != null)
                .ToDictionaryAsync(t => t.BuiltInId!.Value, cancellationToken);

    private async Task<ThemeDefinition?> VersionAsync(Guid? tenantId, ThemeDefinition theme, CancellationToken cancellationToken) =>
        tenantId is null || theme.TenantId is not null
            ? null
            : await db.Set<ThemeDefinition>().AsNoTracking().SingleOrDefaultAsync(t => t.TenantId == tenantId && t.BuiltInId == theme.Id, cancellationToken);

    private Task<ThemeDefinition?> StoredVersionAsync(ThemeDefinition builtIn, CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId
            ? db.Set<ThemeDefinition>().SingleOrDefaultAsync(t => t.TenantId == tenantId && t.BuiltInId == builtIn.Id, cancellationToken)
            : Task.FromResult<ThemeDefinition?>(null);

    // the organisation's version of a built-in theme; a new one starts with the built-in look, published
    private async Task<ThemeDefinition?> VersionForAsync(ThemeDefinition builtIn, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return null;
        }

        if (await StoredVersionAsync(builtIn, cancellationToken) is { } existing)
        {
            return existing;
        }

        var version = new ThemeDefinition
        {
            Name = builtIn.Name, TenantId = tenantId, BuiltInId = builtIn.Id, IsPublished = true, PaletteLight = builtIn.PaletteLight, PaletteDark = builtIn.PaletteDark,
        };
        Copy(builtIn, version);
        db.Add(version);
        return version;
    }

    private async Task<ThemeDefinition?> EffectiveAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var chosen = tenantId is null
            ? null
            : await (from mapping in db.Set<TenantTheme>()
                     join theme in db.Set<ThemeDefinition>() on mapping.ThemeId equals theme.Id
                     where mapping.TenantId == tenantId && (theme.TenantId == null || theme.TenantId == tenantId)
                     select theme).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return chosen ?? await db.Set<ThemeDefinition>().AsNoTracking()
            .Where(t => t.TenantId == null)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> SystemTenantIdAsync(CancellationToken cancellationToken) =>
        services.GetService<ITenantDirectory>() is { } tenants ? await tenants.GetSystemTenantIdAsync(cancellationToken) : null;

    private static void Apply(ThemeDefinition theme, ThemeRequest request)
    {
        theme.Name = request.Name.Trim();
        theme.PaletteLight = request.PaletteLight.GetRawText();
        theme.PaletteDark = request.PaletteDark.GetRawText();
        theme.Typography = Raw(request.Typography);
        theme.LayoutProperties = Raw(request.LayoutProperties);
        theme.Shadows = Raw(request.Shadows);
        theme.Options = Raw(request.Options);
        theme.IsPublished = request.IsPublished;
        theme.Logo = string.IsNullOrWhiteSpace(request.Logo) ? null : request.Logo.Trim();
        theme.CustomCss = string.IsNullOrWhiteSpace(request.CustomCss) ? null : request.CustomCss;
    }

    private static void Copy(ThemeDefinition from, ThemeDefinition to)
    {
        to.PaletteLight = from.PaletteLight;
        to.PaletteDark = from.PaletteDark;
        to.Typography = from.Typography;
        to.LayoutProperties = from.LayoutProperties;
        to.Shadows = from.Shadows;
        to.Options = from.Options;
        to.Logo = from.Logo;
        to.CustomCss = from.CustomCss;
    }

    private static string? Raw(JsonElement? element) =>
        element is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value ? value.GetRawText() : null;

    // a built-in theme with the organisation's version: its look when changed, its publishing, and a revision that grows with both
    private static ThemeDto ToDto(ThemeDefinition theme, bool isDefault, ThemeDefinition? version)
    {
        var look = version is { IsCustomized: true } ? version : theme;
        return new ThemeDto(
            theme.Id,
            theme.Name,
            theme.TenantId is null,
            isDefault,
            Parse(look.PaletteLight)!.Value,
            Parse(look.PaletteDark)!.Value,
            Parse(look.Typography),
            Parse(look.LayoutProperties),
            look.Logo,
            look.CustomCss,
            theme.Revision + (version?.Revision ?? 0),
            Parse(look.Shadows),
            Parse(look.Options),
            version?.IsPublished ?? (theme.TenantId is null || theme.IsPublished),
            version?.IsCustomized ?? false);
    }

    private static JsonElement? Parse(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
