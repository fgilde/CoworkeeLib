using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Localization.Languages;

/// <summary>Stores the configured languages at setup, so they show on the Languages page from the start.</summary>
internal sealed class LanguageSetupStep(CoworkeeDbContext db, LocalizationResources resources, IOptions<LocalizationOptions> options) : ISetupStep
{
    public async Task<Error?> ApplyAsync(CompleteSetupRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        if (options.Value.Languages.Count > 0 && !await db.Set<Language>().AnyAsync(cancellationToken))
        {
            db.AddRange(InitialLanguages.Create(options.Value, resources));
        }

        return null;
    }
}
