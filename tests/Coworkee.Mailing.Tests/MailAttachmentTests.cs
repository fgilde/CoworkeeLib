using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed class MailAttachmentTests(MailApp app) : IAsyncLifetime
{
    private static readonly MailAttachment Sheet = new("Sales.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1, 2, 3]);

    private SetupResultDto _setup = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Attachments_are_kept_with_the_mail_and_reach_the_transport()
    {
        var id = await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async sp =>
        {
            var mailId = await sp.GetRequiredService<IMailSender>().QueueWithAttachmentsAsync("ada@acme.test", "Test.Hello", new { user = new { first_name = "Ada" } }, "en", [Sheet], Ct);
            await sp.GetRequiredService<MailTestDbContext>().SaveChangesAsync(Ct);
            return mailId;
        });
        var transport = new Recording();

        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async sp =>
        {
            await ActivatorUtilities.CreateInstance<SendMailJob>(sp, (IEnumerable<IMailTransport>)[transport]).ExecuteAsync(id, Ct);
            return 0;
        });

        var file = transport.Sent.ShouldHaveSingleItem().Files.ShouldHaveSingleItem();
        file.FileName.ShouldBe("Sales.xlsx");
        file.Content.ShouldBe(Sheet.Content);
        (await app.InDbAsync(db => db.Set<OutgoingMailAttachment>().CountAsync(a => a.MailId == id, Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task Attachments_above_the_limit_are_refused() =>
        await Should.ThrowAsync<ArgumentException>(() => app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, sp =>
            sp.GetRequiredService<IMailSender>().QueueWithAttachmentsAsync("ada@acme.test", "Test.Hello", new { }, "en",
                [Sheet with { Content = new byte[MailAttachment.MaxTotalBytes + 1] }], Ct)));

    private sealed class Recording : IMailTransport
    {
        public List<MailEnvelope> Sent { get; } = [];

        public string Provider => MailProviders.Smtp;

        public Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
        {
            Sent.Add(mail);
            return Task.CompletedTask;
        }
    }
}
