using System.Text.RegularExpressions;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal static partial class CustomMailTemplates
{
    public static readonly Error SystemOnly = Error.Forbidden("mail.template_global_system_only", "Only the system organisation manages global mail templates.");

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    public static partial Regex NamePattern();

    public static async Task<CustomMailTemplate?> FindCustomAsync(this CoworkeeDbContext db, Guid? tenantId, string name, CancellationToken cancellationToken) =>
        (await db.Set<CustomMailTemplate>().Where(t => t.Name == name && (t.TenantId == null || t.TenantId == tenantId)).ToListAsync(cancellationToken))
            .OrderByDescending(t => t.TenantId != null).FirstOrDefault();
}
