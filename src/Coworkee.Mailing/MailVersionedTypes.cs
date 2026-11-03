namespace Coworkee.Mailing;

internal sealed class MailVersionedTypes : Coworkee.Infrastructure.Versioning.IVersionedTypeContributor
{
    public void Define(Coworkee.Infrastructure.Versioning.VersionedTypeContext context) =>
        context.Add<MailTemplateOverride>("MailTemplate", Coworkee.Contracts.Mailing.MailPermissions.Templates.Manage);
}
