using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Theming;

/// <summary>Makes the theme chosen in setup the organisation's default; only a built-in one, there is nothing else yet.</summary>
internal sealed class ThemeSetupStep(CoworkeeDbContext db, IOptions<ThemingOptions> options) : Coworkee.Application.Setup.ISetupStep
{
    public async Task<Error?> ApplyAsync(Coworkee.Contracts.Identity.CompleteSetupRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        if (request.ThemeId is not { } themeId)
        {
            return null;
        }

        await ThemeSeeds.EnsureAsync(db, options.Value, cancellationToken);
        if (!await db.Set<ThemeDefinition>().AnyAsync(t => t.Id == themeId && t.TenantId == null, cancellationToken))
        {
            return Error.Validation("ThemeId", "Choose one of the offered themes.");
        }

        db.Add(new TenantTheme { TenantId = tenantId, ThemeId = themeId });
        return null;
    }
}
