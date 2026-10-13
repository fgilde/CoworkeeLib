using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed class MailProviderTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _otherUser;
    private Guid _otherTenant;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        (_otherUser, _otherTenant) = await app.CreateTenantAdminAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task PutGlobalAsync(params (string Name, string Value)[] values) => PutAsync(app.As(_setup.AdminUserId, _setup.TenantId), "global", values);

    private Task PutOtherAsync(params (string Name, string Value)[] values) => PutAsync(app.As(_otherUser, _otherTenant), "tenant", values);

    private static async Task PutAsync(HttpClient client, string scope, (string Name, string Value)[] values) =>
        (await client.PutAsJsonAsync($"/api/v1/settings/{scope}", new SetSettingsRequest(values.ToDictionary(v => v.Name, v => (string?)v.Value)), Ct)).EnsureSuccessStatusCode();

    private Task<MailConnection> ResolveAsync(Guid? userId, Guid? tenantId) =>
        app.AsActorAsync(userId, tenantId, sp => sp.GetRequiredService<MailConnectionResolver>().ResolveAsync(Ct));

    [Fact]
    public async Task Global_connection_defaults_to_smtp_from_the_configured_server()
    {
        var connection = await ResolveAsync(_setup.AdminUserId, _setup.TenantId);

        connection.Provider.ShouldBe(MailProviders.Smtp);
        connection[MailSettings.Host].ShouldNotBeNullOrWhiteSpace();
        connection[MailSettings.From].ShouldBe("noreply@coworkee.test");
    }

    [Fact]
    public async Task Organisation_without_own_server_uses_global_values_only()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.SendGrid), (MailSettings.ApiKey, "global-key"), (MailSettings.From, "global@acme.test"));
        await PutOtherAsync((MailSettings.Provider, MailProviders.Mailchimp), (MailSettings.ApiKey, "tenant-key"), (MailSettings.From, "tenant@other.test"));

        var connection = await ResolveAsync(_otherUser, _otherTenant);

        connection.Provider.ShouldBe(MailProviders.SendGrid);
        connection[MailSettings.ApiKey].ShouldBe("global-key");
        connection[MailSettings.From].ShouldBe("global@acme.test");
    }

    [Fact]
    public async Task Organisation_with_own_server_uses_tenant_values_only()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.SendGrid), (MailSettings.ApiKey, "global-key"), (MailSettings.Whitelist, "@acme.test"));
        await PutOtherAsync((MailSettings.UseOwnServer, "true"), (MailSettings.Provider, MailProviders.Mailchimp), (MailSettings.ApiKey, "tenant-key"), (MailSettings.From, "tenant@other.test"));

        var connection = await ResolveAsync(_otherUser, _otherTenant);

        connection.Provider.ShouldBe(MailProviders.Mailchimp);
        connection[MailSettings.ApiKey].ShouldBe("tenant-key");
        connection[MailSettings.From].ShouldBe("tenant@other.test");
        connection[MailSettings.Whitelist].ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Switching_the_own_server_off_keeps_the_tenant_values_and_returns_to_global()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.SendGrid), (MailSettings.ApiKey, "global-key"));
        await PutOtherAsync((MailSettings.UseOwnServer, "true"), (MailSettings.Provider, MailProviders.Mailchimp), (MailSettings.ApiKey, "tenant-key"));
        await PutOtherAsync((MailSettings.UseOwnServer, "false"));

        (await ResolveAsync(_otherUser, _otherTenant))[MailSettings.ApiKey].ShouldBe("global-key");
        await PutOtherAsync((MailSettings.UseOwnServer, "true"));
        (await ResolveAsync(_otherUser, _otherTenant))[MailSettings.ApiKey].ShouldBe("tenant-key");
    }

    [Fact]
    public async Task Own_server_with_incomplete_configuration_fails_instead_of_falling_back_to_global()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.Mailgun), (MailSettings.ApiKey, "global-key"), (MailSettings.MailgunDomain, "mg.acme.test"));
        await PutOtherAsync((MailSettings.UseOwnServer, "true"), (MailSettings.Provider, MailProviders.Mailgun), (MailSettings.ApiKey, "tenant-key"));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => ResolveAsync(_otherUser, _otherTenant));

        error.Message.ShouldBe("Mailgun domain is not configured.");
    }

    [Fact]
    public async Task Own_server_without_stored_values_does_not_pass_on_deployment_defaults()
    {
        await PutOtherAsync((MailSettings.UseOwnServer, "true"));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => ResolveAsync(_otherUser, _otherTenant));

        error.Message.ShouldBe("SMTP host is not configured.");
        (await ResolveAsync(_setup.AdminUserId, _setup.TenantId))[MailSettings.Host].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Setup_check_warns_when_no_transport_serves_the_provider()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.SendGrid), (MailSettings.ApiKey, "global-key"));

        var none = await RunSetupCheckAsync();
        var all = await app.AsActorAsync(null, null, sp => sp.GetRequiredService<Coworkee.Application.Setup.ISetupCheck>().RunAsync(Ct));

        none.Status.ShouldBe(SetupCheckStatus.Warning);
        none.Message.ShouldContain("sendGrid");
        all.Status.ShouldBe(SetupCheckStatus.Ok);
    }

    [Theory]
    [InlineData(MailProviders.SendGrid)]
    [InlineData(MailProviders.Mailchimp)]
    [InlineData(MailProviders.Mailgun)]
    public async Task Provider_without_its_credentials_is_not_configured(string provider)
    {
        await PutGlobalAsync((MailSettings.Provider, provider));

        (await Should.ThrowAsync<InvalidOperationException>(() => ResolveAsync(_setup.AdminUserId, _setup.TenantId))).Message.ShouldBe("Mail API key is not configured.");
    }

    [Fact]
    public async Task System_organisation_always_uses_global()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.SendGrid), (MailSettings.ApiKey, "global-key"));
        await PutAsync(app.As(_setup.AdminUserId, _setup.TenantId), "tenant", [(MailSettings.UseOwnServer, "true"), (MailSettings.Provider, MailProviders.Mailchimp)]);

        (await ResolveAsync(_setup.AdminUserId, _setup.TenantId)).Provider.ShouldBe(MailProviders.SendGrid);
    }

    [Fact]
    public async Task Job_sends_through_the_transport_of_the_organisations_provider_in_its_tenant_context()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.SendGrid), (MailSettings.ApiKey, "global-key"));
        await PutOtherAsync((MailSettings.UseOwnServer, "true"), (MailSettings.Provider, MailProviders.Mailgun), (MailSettings.ApiKey, "tenant-key"),
            (MailSettings.MailgunDomain, "mg.other.test"), (MailSettings.From, "tenant@other.test"));
        var sendGrid = new RecordingTransport(MailProviders.SendGrid);
        var mailgun = new RecordingTransport(MailProviders.Mailgun);
        var id = await QueueAsync("ada@other.test");

        await RunJobAsync(id, _otherUser, _otherTenant, sendGrid, mailgun);

        sendGrid.Sent.ShouldBeEmpty();
        var (mail, connection) = mailgun.Sent.ShouldHaveSingleItem();
        mail.From.ShouldBe("tenant@other.test");
        mail.To.ShouldBe("ada@other.test");
        connection[MailSettings.MailgunDomain].ShouldBe("mg.other.test");
        (await StatusAsync(id)).ShouldBe(OutgoingMailStatus.Sent);
    }

    [Fact]
    public async Task Job_without_a_transport_for_the_provider_records_a_clear_error()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.SendGrid), (MailSettings.ApiKey, "global-key"));
        var id = await QueueAsync("ada@acme.test");

        await Should.ThrowAsync<InvalidOperationException>(() => RunJobAsync(id, _setup.AdminUserId, _setup.TenantId));

        var mail = await app.InDbAsync(db => db.Set<OutgoingMail>().SingleAsync(m => m.Id == id, Ct));
        mail.LastError.ShouldBe("No mail transport is registered for provider 'sendGrid'.");
    }

    [Fact]
    public async Task Job_with_incomplete_configuration_records_not_configured()
    {
        await PutGlobalAsync((MailSettings.Provider, MailProviders.Mailgun), (MailSettings.ApiKey, "global-key"));
        var id = await QueueAsync("ada@acme.test");

        await Should.ThrowAsync<InvalidOperationException>(() => RunJobAsync(id, _setup.AdminUserId, _setup.TenantId));

        (await app.InDbAsync(db => db.Set<OutgoingMail>().SingleAsync(m => m.Id == id, Ct))).LastError.ShouldBe("Mailgun domain is not configured.");
    }

    private Task<SetupCheckDto> RunSetupCheckAsync() =>
        app.AsActorAsync(null, null, sp => ActivatorUtilities.CreateInstance<MailSetupCheck>(sp, (IEnumerable<IMailTransport>)[]).RunAsync(Ct));

    private Task<Guid> QueueAsync(string to) => app.InDbAsync(async db =>
    {
        var tenant = to.EndsWith("@other.test", StringComparison.Ordinal) ? _otherTenant : _setup.TenantId;
        var mail = OutgoingMail.Queue(to, new RenderedMail("Hello", "<p>hi</p>"), "Test.Hello", tenant, DateTimeOffset.UtcNow);
        mail.ClearDomainEvents();
        db.Add(mail);
        await db.SaveChangesAsync(Ct);
        return mail.Id;
    });

    private Task<int> RunJobAsync(Guid id, Guid userId, Guid tenantId, params IMailTransport[] transports) =>
        app.AsActorAsync(userId, tenantId, async sp =>
        {
            await ActivatorUtilities.CreateInstance<SendMailJob>(sp, (IEnumerable<IMailTransport>)transports).ExecuteAsync(id, Ct);
            return 0;
        });

    private Task<OutgoingMailStatus> StatusAsync(Guid id) =>
        app.InDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Id == id).Select(m => m.Status).SingleAsync(Ct));

    private sealed class RecordingTransport(string provider) : IMailTransport
    {
        public List<(MailEnvelope Mail, MailConnection Connection)> Sent { get; } = [];

        public string Provider => provider;

        public Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
        {
            Sent.Add((mail, connection));
            return Task.CompletedTask;
        }
    }
}
