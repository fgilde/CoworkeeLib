using System.Net.Http.Headers;
using System.Text;
using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.AspNetCore.Tests;

public sealed class BlobContentResultTests
{
    [Fact]
    public async Task Inline_content_is_sandboxed_and_downloads_carry_the_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapGet("/blob", (bool download) => new BlobContentResult(new MemoryStream(Encoding.UTF8.GetBytes("<b>hi</b>")), "text/html", "page.html", download));
        await app.StartAsync(ct);
        var client = app.GetTestClient();

        using var inline = await client.GetAsync("/blob?download=false", ct);
        inline.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        inline.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(BlobContentResult.SandboxPolicy);
        inline.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        inline.Headers.CacheControl!.NoStore.ShouldBeTrue();
        inline.Content.Headers.ContentDisposition.ShouldBeNull();
        (await inline.Content.ReadAsStringAsync(ct)).ShouldBe("<b>hi</b>");

        using var download = await client.GetAsync("/blob?download=true", ct);
        download.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
        download.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        download.Content.Headers.ContentDisposition!.FileName.ShouldBe("page.html");

        var range = new HttpRequestMessage(HttpMethod.Get, "/blob?download=false") { Headers = { Range = new RangeHeaderValue(0, 1) } };
        using var partial = await client.SendAsync(range, ct);
        (await partial.Content.ReadAsStringAsync(ct)).ShouldBe("<b");
    }
}
