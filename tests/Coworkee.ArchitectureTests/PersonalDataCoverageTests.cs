using System.Reflection;
using System.Text.RegularExpressions;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.ArchitectureTests;

/// <summary>
/// Every column of the library's tables that names a user (an id or an address) is taken care of when a user is exported or erased. A new one fails here
/// until it is added below with the contributor that handles it, and to the table in security/privacy.md.
/// </summary>
public sealed partial class PersonalDataCoverageTests
{
    private const string References = "PersonalDataReferences (tombstone)";

    // column -> who exports and erases it
    private static readonly Dictionary<string, string> Covered = new(StringComparer.Ordinal)
    {
        ["AccessToken.UserId"] = "PersonalDataReferences (deleted)",
        ["AiChatSession.UserId"] = "AiSessionPersonalData",
        ["AiToolCall.UserId"] = "AiPersonalData",
        ["ApiKey.RevokedBy"] = "ApiKeyPersonalData (tombstone)",
        ["ApiKey.UserId"] = "ApiKeyPersonalData",
        ["AuditEntry.ActorId"] = "AuditPersonalData / PersonalDataReferences",
        ["AuditEntry.ImpersonatorId"] = "AuditPersonalData / PersonalDataReferences",
        ["ChatAttachment.UploadedBy"] = "ChatPersonalData",
        ["ChatMember.UserId"] = "ChatPersonalData",
        ["ChatMessage.FromUserId"] = "ChatPersonalData",
        ["ChatMessage.ToUserId"] = "ChatPersonalData",
        ["ChatReaction.UserId"] = "ChatPersonalData",
        ["Comment.AuthorId"] = "SocialPersonalData",
        ["DashboardReport.UserId"] = "ReportPersonalData",
        ["DataTransferJob.UserId"] = "DataTransferPersonalData",
        ["ExportSchedule.Recipients"] = "DataTransferPersonalData (address removed)",
        ["ExportSchedule.UserId"] = "DataTransferPersonalData",
        ["GroupSelection.UserId"] = "IdentityPersonalData (cascade)",
        ["IdentityUserClaim`1.UserId"] = "IdentityPersonalData (cascade)",
        ["IdentityUserLogin`1.UserId"] = "IdentityPersonalData (cascade)",
        ["IdentityUserPasskey`1.UserId"] = "IdentityPersonalData (cascade)",
        ["IdentityUserRole`1.UserId"] = "IdentityPersonalData (cascade)",
        ["IdentityUserToken`1.UserId"] = "IdentityPersonalData (cascade)",
        ["NotificationDigestState.UserId"] = "NotificationPersonalData",
        ["NotificationFollow.UserId"] = "NotificationPersonalData",
        ["NotificationPreference.UserId"] = "NotificationPersonalData",
        ["Notification.UserId"] = "NotificationPersonalData",
        ["OpenIddictEntityFrameworkCoreAuthorization`1.Subject"] = "AuthPersonalData",
        ["OpenIddictEntityFrameworkCoreToken`1.Subject"] = "AuthPersonalData",
        ["OutboxMessage.ActorId"] = "PersonalDataReferences (tombstone)",
        ["OutboxMessage.ImpersonatorId"] = "PersonalDataReferences (tombstone)",
        ["OutgoingMail.To"] = "MailPersonalData",
        ["PendingNotificationMail.UserId"] = "NotificationPersonalData",
        ["PermissionGrant.ProviderKey"] = "IdentityPersonalData",
        ["PushSubscription.UserId"] = "NotificationPersonalData",
        ["Rating.UserId"] = "SocialPersonalData",
        ["Reaction.UserId"] = "SocialPersonalData",
        ["Report.OwnerId"] = "ReportPersonalData",
        ["ReportFavorite.UserId"] = "ReportPersonalData",
        ["ReportSubscription.UserId"] = "ReportPersonalData",
        ["ResourcePermission.PrincipalId"] = "IdentityPersonalData",
        ["RetentionRun.LastRunBy"] = "RetentionPersonalData (tombstone)",
        ["SettingValue.ScopeKey"] = "SettingPersonalData",
        ["User.Email"] = "IdentityPersonalData",
        ["User.NormalizedEmail"] = "IdentityPersonalData",
        ["UserGroupMember.UserId"] = "IdentityPersonalData (cascade)",
        ["UserSession.EndedBy"] = "UserSessionPersonalData (tombstone)",
        ["UserSession.ImpersonatorId"] = "UserSessionPersonalData (tombstone)",
        ["UserSession.UserId"] = "UserSessionPersonalData",
        ["WebhookSubscription.OwnerId"] = "WebhookPersonalData",
        ["WorkflowApproval.RequestedBy"] = "WorkflowPersonalData",
        ["WorkflowApprover.UserId"] = "WorkflowPersonalData",
        ["WorkflowStep.UserId"] = "WorkflowPersonalData",
    };

    // names that look like a user and are none: a mail subject, a workflow state
    private static readonly string[] Unrelated = ["MailTemplateOverride.Subject", "OutgoingMail.Subject", "WorkflowStep.To"];

    private static readonly string[] Bookkeeping = ["CreatedBy", "ModifiedBy", "DeletedBy"];

    [Fact]
    public void Every_column_that_names_a_user_has_a_contributor()
    {
        var found = Model().GetEntityTypes().Where(t => !t.IsOwned())
            .SelectMany(t => t.GetDeclaredProperties().Where(NamesAUser).Select(p => $"{t.ClrType.Name}.{p.Name}"))
            .Except(Unrelated)
            .ToHashSet(StringComparer.Ordinal);

        found.Except(Covered.Keys).Order().ShouldBeEmpty("columns without a personal data contributor");
        Covered.Keys.Except(found).Order().ShouldBeEmpty("stale entries");
    }

    [Fact]
    public void The_bookkeeping_columns_hold_ids_the_references_step_replaces()
    {
        var wrong = Model().GetEntityTypes().Where(t => !t.IsOwned())
            .SelectMany(t => t.GetDeclaredProperties().Where(p => Bookkeeping.Contains(p.Name) && p.ClrType != typeof(Guid) && p.ClrType != typeof(Guid?)))
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}");

        wrong.ShouldBeEmpty(References);
    }

    private static bool NamesAUser(IProperty property) =>
        !Bookkeeping.Contains(property.Name)
        && ((property.ClrType == typeof(Guid) || property.ClrType == typeof(Guid?)) && UserId().IsMatch(property.Name)
            || property.ClrType == typeof(string) && UserText().IsMatch(property.Name)
            || property.ClrType == typeof(List<string>) && property.Name == "Recipients");

    [GeneratedRegex("^(UserId|.*UserId|OwnerId|AuthorId|ActorId|ImpersonatorId|PrincipalId|ProviderKey|ScopeKey|SubscriberId|ApproverId|RecipientId|.+By)$")]
    private static partial Regex UserId();

    [GeneratedRegex("^(Subject|To|Cc|Bcc|Email|NormalizedEmail|EmailAddress)$")]
    private static partial Regex UserText();

    private static IModel Model()
    {
        var contributors = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "Coworkee.*.dll").Select(f => Assembly.LoadFrom(f)))
            .Distinct()
            .Where(a => a.GetName().Name is { } name && name.StartsWith("Coworkee.", StringComparison.Ordinal) && !name.EndsWith("Tests", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IModelContributor).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IModelContributor)Activator.CreateInstance(t)!)
            .ToList();
        var options = new DbContextOptionsBuilder<AllTables>().UseNpgsql("Host=unused").Options;
        using var context = new AllTables(options, contributors);
        return context.Model;
    }

    private sealed class AllTables(DbContextOptions<AllTables> options, IEnumerable<IModelContributor> contributors)
        : CoworkeeDbContext(options, new ImpersonatedUser(null, null), contributors);
}
