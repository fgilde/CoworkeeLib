using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Theming;

public sealed record GetThemes : IQuery<Result<IReadOnlyList<ThemeDto>>>;

public sealed record GetCurrentTheme : IQuery<Result<ThemeDto>>;

[RequiresPermission(ThemePermissions.Manage)]
public sealed record CreateTheme(ThemeRequest Theme) : ICommand<Result<ThemeDto>>;

[RequiresPermission(ThemePermissions.Manage)]
public sealed record UpdateTheme(Guid Id, ThemeRequest Theme) : ICommand<Result<ThemeDto>>;

[RequiresPermission(ThemePermissions.Manage)]
public sealed record DeleteTheme(Guid Id) : ICommand<Result>;

[RequiresPermission(ThemePermissions.Manage)]
public sealed record SetDefaultTheme(Guid Id) : ICommand<Result>;

internal sealed class ThemeHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetThemes, Result<IReadOnlyList<ThemeDto>>>,
      IHandler<GetCurrentTheme, Result<ThemeDto>>,
      IHandler<CreateTheme, Result<ThemeDto>>,
      IHandler<UpdateTheme, Result<ThemeDto>>,
      IHandler<DeleteTheme, Result>,
      IHandler<SetDefaultTheme, Result>
{
    private static readonly Error NotFound = Error.NotFound("themes.not_found", "The theme does not exist.");
    private static readonly Error ReadOnly = Error.Forbidden("themes.read_only", "Built-in themes cannot be changed. Create a copy instead.");

    public async Task<Result<IReadOnlyList<ThemeDto>>> HandleAsync(GetThemes query, CancellationToken cancellationToken)
    {
        await ThemeSeeds.EnsureAsync(db, cancellationToken);
        var defaultId = (await EffectiveAsync(currentUser.TenantId, cancellationToken))?.Id;
        IReadOnlyList<ThemeDto> themes = (await Visible().OrderBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(t => ToDto(t, t.Id == defaultId))
            .ToList();
        return Result<IReadOnlyList<ThemeDto>>.Success(themes);
    }

    public async Task<Result<ThemeDto>> HandleAsync(GetCurrentTheme query, CancellationToken cancellationToken)
    {
        await ThemeSeeds.EnsureAsync(db, cancellationToken);
        var tenantId = currentUser.TenantId ?? await SystemTenantIdAsync(cancellationToken);
        return await EffectiveAsync(tenantId, cancellationToken) is { } theme ? ToDto(theme, true) : NotFound;
    }

    public async Task<Result<ThemeDto>> HandleAsync(CreateTheme command, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Error.Validation("Tenant", "Themes belong to an organisation.");
        }

        if (ThemeValidation.Validate(command.Theme) is { } message)
        {
            return Error.Validation("Theme", message);
        }

        if (await db.Set<ThemeDefinition>().AnyAsync(t => t.TenantId == tenantId && t.Name == command.Theme.Name, cancellationToken))
        {
            return Error.Conflict("themes.name_taken", "A theme with this name already exists.");
        }

        var theme = new ThemeDefinition { Name = command.Theme.Name, TenantId = tenantId, PaletteLight = "{}", PaletteDark = "{}" };
        Apply(theme, command.Theme);
        db.Add(theme);
        return ToDto(theme, false);
    }

    public async Task<Result<ThemeDto>> HandleAsync(UpdateTheme command, CancellationToken cancellationToken)
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

        if (ThemeValidation.Validate(command.Theme) is { } message)
        {
            return Error.Validation("Theme", message);
        }

        Apply(theme, command.Theme);
        return ToDto(theme, false);
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

    private IQueryable<ThemeDefinition> Visible()
    {
        var tenantId = currentUser.TenantId;
        return db.Set<ThemeDefinition>().Where(t => t.TenantId == null || t.TenantId == tenantId);
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
        theme.LogoSvg = string.IsNullOrWhiteSpace(request.LogoSvg) ? null : request.LogoSvg.Trim();
        theme.CustomCss = string.IsNullOrWhiteSpace(request.CustomCss) ? null : request.CustomCss;
    }

    private static string? Raw(JsonElement? element) =>
        element is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value ? value.GetRawText() : null;

    private static ThemeDto ToDto(ThemeDefinition theme, bool isDefault) => new(
        theme.Id,
        theme.Name,
        theme.TenantId is null,
        isDefault,
        Parse(theme.PaletteLight)!.Value,
        Parse(theme.PaletteDark)!.Value,
        Parse(theme.Typography),
        Parse(theme.LayoutProperties),
        theme.LogoSvg,
        theme.CustomCss,
        theme.Revision);

    private static JsonElement? Parse(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
