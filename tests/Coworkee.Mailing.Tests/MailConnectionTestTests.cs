using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed class MailConnectionTestTests(MailApp app) : IAsyncLifetime
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

    private Task PutAsync(HttpClient client, string scope, params (string Name, string Value)[] values) =>
        client.PutAsJsonAsync($"/api/v1/settings/{scope}", new SetSettingsRequest(values.ToDictionary(v => v.Name, v => (string?)v.Value)), Ct);

    private Task<Result<MailConnectionTestDto>> TestAsync(Guid userId, Guid tenantId, Dictionary<string, string?> draft, params IMailTransport[] transports) =>
        TestRequestAsync(userId, tenantId, new MailConnectionTestRequest(draft), transports);

    private Task<Result<MailConnectionTestDto>> TestRequestAsync(Guid userId, Guid tenantId, MailConnectionTestRequest request, params IMailTransport[] transports) =>
        app.AsActorAsync(userId, tenantId, sp => ActivatorUtilities.CreateInstance<TestMailConnectionHandler>(sp, (IEnumerable<IMailTransport>)transports)
            .HandleAsync(new TestMailConnection(request), Ct));

    [Fact]
    public async Task Empty_secret_in_the_draft_uses_the_stored_key_and_mail_goes_to_the_current_user_immediately()
    {
        await PutAsync(app.As(_setup.AdminUserId, _setup.TenantId), "global", (MailSettings.ApiKey, "stored-key"), (MailSettings.From, "stored@acme.test"));
        var transport = new RecordingTransport(MailProviders.SendGrid);

        var result = await TestAsync(_setup.AdminUserId, _setup.TenantId,
            new() { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "", [MailSettings.From] = "draft@acme.test", [MailSettings.Whitelist] = "@nobody.test" }, transport);

        result.Value.ShouldBe(new MailConnectionTestDto(true, null));
        var (mail, connection) = transport.Sent.ShouldHaveSingleItem();
        (mail.To, mail.From, connection[MailSettings.ApiKey]).ShouldBe(("admin@acme.test", "draft@acme.test", "stored-key"));
    }

    [Fact]
    public async Task A_typed_secret_wins_over_the_stored_one()
    {
        await PutAsync(app.As(_setup.AdminUserId, _setup.TenantId), "global", (MailSettings.ApiKey, "stored-key"));
        var transport = new RecordingTransport(MailProviders.Mailchimp);

        await TestAsync(_setup.AdminUserId, _setup.TenantId, new() { [MailSettings.Provider] = MailProviders.Mailchimp, [MailSettings.ApiKey] = "typed-key" }, transport);

        transport.Sent.ShouldHaveSingleItem().Connection[MailSettings.ApiKey].ShouldBe("typed-key");
    }

    [Fact]
    public async Task Other_organisation_tests_its_own_draft_and_never_the_global_key()
    {
        await PutAsync(app.As(_setup.AdminUserId, _setup.TenantId), "global", (MailSettings.ApiKey, "global-key"));
        var transport = new RecordingTransport(MailProviders.SendGrid);

        var missing = await TestAsync(_otherUser, _otherTenant, new() { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "" }, transport);

        missing.Value.ShouldBe(new MailConnectionTestDto(false, "Mail API key is not configured."));
        transport.Sent.ShouldBeEmpty();

        await PutAsync(app.As(_otherUser, _otherTenant), "tenant", (MailSettings.ApiKey, "tenant-key"));
        var stored = await TestAsync(_otherUser, _otherTenant, new() { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "" }, transport);

        stored.Value.Success.ShouldBeTrue();
        transport.Sent.ShouldHaveSingleItem().Connection[MailSettings.ApiKey].ShouldBe("tenant-key");
    }

    [Fact]
    public async Task Failure_is_reported_without_the_typed_secret()
    {
        var transport = new RecordingTransport(MailProviders.SendGrid) { Failure = new InvalidOperationException("401 for key typed-key") };

        var result = await TestAsync(_setup.AdminUserId, _setup.TenantId, new() { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "typed-key" }, transport);

        result.Value.ShouldBe(new MailConnectionTestDto(false, "401 for key ***"));
    }

    [Fact]
    public async Task Stored_secrets_are_redacted_from_the_error()
    {
        await PutAsync(app.As(_setup.AdminUserId, _setup.TenantId), "global", (MailSettings.ApiKey, "stored-key"), (MailSettings.Password, "stored-pw"));
        var transport = new RecordingTransport(MailProviders.SendGrid) { Failure = new InvalidOperationException("rejected stored-key and stored-pw") };

        var result = await TestAsync(_setup.AdminUserId, _setup.TenantId, new() { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "" }, transport);

        result.Value.ShouldBe(new MailConnectionTestDto(false, "rejected *** and ***"));
    }

    [Fact]
    public async Task A_cleared_secret_does_not_fall_back_to_the_stored_one()
    {
        await PutAsync(app.As(_otherUser, _otherTenant), "tenant", (MailSettings.ApiKey, "tenant-key"));
        var transport = new RecordingTransport(MailProviders.SendGrid);

        var result = await TestRequestAsync(_otherUser, _otherTenant,
            new MailConnectionTestRequest(new Dictionary<string, string?> { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "" }, [MailSettings.ApiKey]), transport);

        result.Value.ShouldBe(new MailConnectionTestDto(false, "Mail API key is not configured."));
        transport.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Organisations_get_only_the_exception_type_of_a_send_failure()
    {
        var transport = new RecordingTransport(MailProviders.SendGrid) { Failure = new InvalidOperationException("connect to 10.0.0.5:25 refused") };

        var result = await TestAsync(_otherUser, _otherTenant, new() { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "typed-key" }, transport);

        result.Value.ShouldBe(new MailConnectionTestDto(false, "The test mail could not be sent. (InvalidOperationException)"));
    }

    [Fact]
    public async Task Provider_without_a_transport_is_reported()
    {
        var result = await TestAsync(_setup.AdminUserId, _setup.TenantId, new() { [MailSettings.Provider] = MailProviders.SendGrid, [MailSettings.ApiKey] = "typed-key" });

        result.Value.ShouldBe(new MailConnectionTestDto(false, "No mail transport is registered for provider 'sendGrid'."));
    }

    [Fact]
    public async Task Endpoint_sends_through_the_registered_smtp_transport_and_needs_the_settings_permission()
    {
        var response = await app.As(_setup.AdminUserId, _setup.TenantId)
            .PostAsJsonAsync("/api/v1/mail/connection/test", new MailConnectionTestRequest(new Dictionary<string, string?> { [MailSettings.Provider] = MailProviders.Smtp }), Ct);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<MailConnectionTestDto>(Ct)).ShouldBe(new MailConnectionTestDto(true, null));
        await MailApp.Eventually(async () => (await app.MessagesToAsync("admin@acme.test")).Any(m => m.Subject == "Coworkee test mail"));
        var created = await app.As(_setup.AdminUserId, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await created.Content.ReadFromJsonAsync<UserDto>(Ct))!;
        (await app.As(bob.Id, _setup.TenantId).PostAsJsonAsync("/api/v1/mail/connection/test", new MailConnectionTestRequest(new Dictionary<string, string?>()), Ct))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
    }

    private sealed class RecordingTransport(string provider) : IMailTransport
    {
        public List<(MailEnvelope Mail, MailConnection Connection)> Sent { get; } = [];

        public Exception? Failure { get; init; }

        public string Provider => provider;

        public Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Sent.Add((mail, connection));
            return Task.CompletedTask;
        }
    }
}
