using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed class MailCampaignDomainTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queue_with_campaign_sets_the_campaign_id()
    {
        var campaignId = Guid.NewGuid();
        var to = $"{Guid.NewGuid():N}@allowed.test";

        var mailId = await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async sp =>
        {
            var id = await sp.GetRequiredService<IMailSender>().QueueAsync(to, "Test.Hello", new { user = new { first_name = "Ada" } }, "en", campaignId, Ct);
            await sp.GetRequiredService<MailTestDbContext>().SaveChangesAsync(Ct);
            return id;
        });

        var stored = await app.InDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Id == mailId).Select(m => m.CampaignId).SingleAsync(Ct));
        stored.ShouldBe(campaignId);
    }

    [Fact]
    public void Newsletter_setting_is_defined_with_default_true()
    {
        var definition = app.App.Services.GetRequiredService<ISettingDefinitionManager>().Find(MailSettings.Newsletters);

        definition.ShouldNotBeNull();
        definition.ShouldSatisfyAllConditions(
            d => d.Type.ShouldBe(SettingType.Bool),
            d => d.DefaultValue.ShouldBe("true"),
            d => d.IsClientVisible.ShouldBeTrue(),
            d => d.Scopes.ShouldBe([SettingScope.Global, SettingScope.User], ignoreOrder: true));
    }

    [Fact]
    public void Campaign_permission_is_registered()
    {
        var permissions = app.App.Services.GetRequiredService<IPermissionDefinitionManager>();

        permissions.Exists(MailPermissions.Campaigns.Send).ShouldBeTrue();
        permissions.All.Single(p => p.Name == MailPermissions.Campaigns.Send).Group.ShouldBe(MailPermissions.GroupName);
    }
}
