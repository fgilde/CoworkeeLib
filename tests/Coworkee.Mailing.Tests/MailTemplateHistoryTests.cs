using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing.Tests;

public sealed class MailTemplateHistoryTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Every_save_is_a_version_newest_first_and_a_reset_keeps_them()
    {
        const string template = "/api/v1/mail/templates/Test.Hello/nl";
        (await Admin.PutAsJsonAsync(template, new SaveMailTemplateRequest("One", "<p>1</p>"), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync(template, new SaveMailTemplateRequest("One", "<p>2</p>"), Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync(template, Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync(template, new SaveMailTemplateRequest("Three", "<p>3</p>"), Ct)).EnsureSuccessStatusCode();

        var history = (await Admin.GetFromJsonAsync<MailTemplateVersionDto[]>(template + "/history", Ct))!;

        history.Select(v => (v.Subject, v.Body)).ShouldBe([("Three", "<p>3</p>"), ("One", "<p>2</p>"), ("One", "<p>1</p>")]);
        history.ShouldAllBe(v => v.Author != null);
        (await Admin.GetFromJsonAsync<MailTemplateVersionDto[]>("/api/v1/mail/templates/Test.Hello/sv/history", Ct))!.ShouldBeEmpty();
    }
}
