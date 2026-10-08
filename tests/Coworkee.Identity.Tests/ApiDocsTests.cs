using System.Net;

namespace Coworkee.Identity.Tests;

public sealed class ApiDocsTests(IdentityApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Swagger_ui_requires_the_permission_while_the_document_stays_anonymous()
    {
        await app.ResetAllAsync();
        var setup = await app.SetupAsync();

        (await app.Anonymous().GetAsync("/openapi/v1.json", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.Anonymous().GetAsync("/swagger/index.html", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await app.As(Guid.CreateVersion7(), setup.TenantId).GetAsync("/swagger/index.html", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await app.As(setup.AdminUserId, setup.TenantId).GetAsync("/swagger/index.html", Ct);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Ct)).ShouldContain("theme-toggle");
    }

    [Fact]
    public async Task Openapi_document_declares_the_bearer_scheme() =>
        (await app.Anonymous().GetStringAsync("/openapi/v1.json", Ct)).ShouldContain("\"bearerFormat\": \"JWT\"");
}
