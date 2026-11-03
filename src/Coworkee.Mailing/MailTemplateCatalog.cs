using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal sealed class MailTemplateCatalog(IMailTemplateDefinitionManager registered, CoworkeeDbContext db, ICurrentUser currentUser) : IMailTemplateCatalog
{
    private static readonly IReadOnlyDictionary<string, MailTemplateContent> NoDefaults = new Dictionary<string, MailTemplateContent>();

    public async Task<MailTemplateDefinition?> FindAsync(string name, CancellationToken cancellationToken)
    {
        if (registered.Find(name) is { } definition)
        {
            return definition;
        }

        var custom = (await Visible().Where(t => t.Name == name).ToListAsync(cancellationToken)).OrderByDescending(t => t.TenantId != null).FirstOrDefault();
        return custom is null ? null : Define(custom);
    }

    public async Task<IReadOnlyList<MailTemplateDefinition>> AllAsync(CancellationToken cancellationToken) =>
        [.. registered.All, .. (await Visible().ToListAsync(cancellationToken)).Select(Define)];

    public bool IsCustom(MailTemplateDefinition definition) => registered.Find(definition.Name) is null;

    private IQueryable<CustomMailTemplate> Visible()
    {
        var tenantId = currentUser.TenantId;
        return db.Set<CustomMailTemplate>().AsNoTracking().Where(t => t.TenantId == null || t.TenantId == tenantId);
    }

    private static MailTemplateDefinition Define(CustomMailTemplate template) =>
        new(template.Name, template.DisplayName, MailRecipientModel.Sample, NoDefaults);
}
