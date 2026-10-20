using System.Net;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing.Tests;

public sealed class MailTransportTests
{
    private const string ApiKey = "secret-key-123";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly MailEnvelope Mail = new("Coworkee <noreply@acme.test>", "ada@acme.test", "Hello", "<p>Hi</p>");

    private static MailConnection Connection(string provider, params (string Name, string Value)[] values) =>
        new(provider, values.ToDictionary(v => v.Name, v => (string?)v.Value));

    [Fact]
    public async Task SendGrid_posts_json_with_bearer_key()
    {
        var http = new FakeHttp(HttpStatusCode.Accepted, string.Empty);

        await new SendGridMailTransport(http).SendAsync(Mail, Connection(MailProviders.SendGrid, (MailSettings.ApiKey, ApiKey)), Ct);

        http.Url.ShouldBe("https://api.sendgrid.com/v3/mail/send");
        http.Authorization.ShouldBe($"Bearer {ApiKey}");
        var body = JsonDocument.Parse(http.Body).RootElement;
        body.GetProperty("personalizations")[0].GetProperty("to")[0].GetProperty("email").GetString().ShouldBe("ada@acme.test");
        body.GetProperty("from").GetProperty("email").GetString().ShouldBe("noreply@acme.test");
        body.GetProperty("from").GetProperty("name").GetString().ShouldBe("Coworkee");
        body.GetProperty("subject").GetString().ShouldBe("Hello");
        body.GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("text/html");
        body.GetProperty("content")[0].GetProperty("value").GetString().ShouldBe("<p>Hi</p>");
    }

    [Theory]
    [InlineData("us", "https://api.mailgun.net/v3/mg.acme.test/messages")]
    [InlineData("eu", "https://api.eu.mailgun.net/v3/mg.acme.test/messages")]
    public async Task Mailgun_posts_a_form_with_basic_auth_to_the_region(string region, string url)
    {
        var http = new FakeHttp(HttpStatusCode.OK, "{}");
        var connection = Connection(MailProviders.Mailgun, (MailSettings.ApiKey, ApiKey), (MailSettings.MailgunDomain, "mg.acme.test"), (MailSettings.MailgunRegion, region));

        await new MailgunMailTransport(http).SendAsync(Mail, connection, Ct);

        http.Url.ShouldBe(url);
        http.Authorization.ShouldBe($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{ApiKey}"))}");
        http.ContentType.ShouldBe("application/x-www-form-urlencoded");
        var form = System.Web.HttpUtility.ParseQueryString(http.Body);
        form["from"].ShouldBe("Coworkee <noreply@acme.test>");
        form["to"].ShouldBe("ada@acme.test");
        form["subject"].ShouldBe("Hello");
        form["html"].ShouldBe("<p>Hi</p>");
    }

    [Fact]
    public async Task Mailchimp_posts_key_and_message_as_json()
    {
        var http = new FakeHttp(HttpStatusCode.OK, """[{"email":"ada@acme.test","status":"queued","_id":"1"}]""");

        await new MailchimpMailTransport(http).SendAsync(Mail, Connection(MailProviders.Mailchimp, (MailSettings.ApiKey, ApiKey)), Ct);

        http.Url.ShouldBe("https://mandrillapp.com/api/1.0/messages/send");
        var body = JsonDocument.Parse(http.Body).RootElement;
        body.GetProperty("key").GetString().ShouldBe(ApiKey);
        var message = body.GetProperty("message");
        message.GetProperty("html").GetString().ShouldBe("<p>Hi</p>");
        message.GetProperty("subject").GetString().ShouldBe("Hello");
        message.GetProperty("from_email").GetString().ShouldBe("noreply@acme.test");
        message.GetProperty("to")[0].GetProperty("email").GetString().ShouldBe("ada@acme.test");
        message.GetProperty("to")[0].GetProperty("type").GetString().ShouldBe("to");
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("invalid")]
    public async Task Mailchimp_rejection_in_a_successful_response_is_an_error(string status)
    {
        var http = new FakeHttp(HttpStatusCode.OK, $$"""[{"email":"ada@acme.test","status":"{{status}}","reject_reason":"hard-bounce"}]""");

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            new MailchimpMailTransport(http).SendAsync(Mail, Connection(MailProviders.Mailchimp, (MailSettings.ApiKey, ApiKey)), Ct));

        error.Message.ShouldContain(status);
        error.Message.ShouldContain("hard-bounce");
    }

    [Theory]
    [InlineData(MailProviders.SendGrid)]
    [InlineData(MailProviders.Mailgun)]
    [InlineData(MailProviders.Mailchimp)]
    public async Task Error_response_names_the_status_and_never_the_key(string provider)
    {
        var http = new FakeHttp(HttpStatusCode.Unauthorized, $$"""{"message":"Invalid key {{ApiKey}}"}""");
        IMailTransport transport = provider switch
        {
            MailProviders.SendGrid => new SendGridMailTransport(http),
            MailProviders.Mailgun => new MailgunMailTransport(http),
            _ => new MailchimpMailTransport(http),
        };

        var error = await Should.ThrowAsync<InvalidOperationException>(() => transport.SendAsync(Mail,
            Connection(provider, (MailSettings.ApiKey, ApiKey), (MailSettings.MailgunDomain, "mg.acme.test")), Ct));

        error.Message.ShouldContain("401");
        error.Message.ShouldContain("Invalid key");
        error.Message.ShouldNotContain(ApiKey);
    }

    [Theory]
    [InlineData("""{"status":"error","message":"x"}""")]
    [InlineData("[]")]
    [InlineData("[1]")]
    [InlineData("""[{"email":"ada@acme.test"}]""")]
    [InlineData("not json")]
    public async Task Mailchimp_unexpected_success_response_is_an_error(string response)
    {
        var http = new FakeHttp(HttpStatusCode.OK, response);

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            new MailchimpMailTransport(http).SendAsync(Mail, Connection(MailProviders.Mailchimp, (MailSettings.ApiKey, ApiKey)), Ct));

        error.Message.ShouldBe("Mailchimp returned an unexpected response.");
    }

    [Theory]
    [InlineData("""{"message":"bad key sec\"ret+key/1&x"}""")]
    [InlineData("error=sec%22ret%2Bkey%2F1%26x")]
    [InlineData("error=sec%22ret%2bkey%2f1%26x")]
    public async Task Error_response_redacts_the_key_also_encoded(string response)
    {
        var http = new FakeHttp(HttpStatusCode.BadRequest, response);
        var key = "sec\"ret+key/1&x";

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            new SendGridMailTransport(http).SendAsync(Mail, Connection(MailProviders.SendGrid, (MailSettings.ApiKey, key)), Ct));

        error.Message.ShouldContain("400");
        error.Message.ShouldContain("***");
        error.Message.ShouldNotContain("ret");
    }

    [Fact]
    public async Task Error_response_redacts_the_basic_auth_form_of_the_key()
    {
        var http = new FakeHttp(HttpStatusCode.Unauthorized, "echo " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"api:{ApiKey}")));

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            new MailgunMailTransport(http).SendAsync(Mail, Connection(MailProviders.Mailgun, (MailSettings.ApiKey, ApiKey), (MailSettings.MailgunDomain, "mg.acme.test")), Ct));

        error.Message.ShouldBe("Mailgun rejected the mail with 401 Unauthorized: echo ***");
    }

    [Fact]
    public async Task Http_providers_send_the_files_base64_or_as_form_parts()
    {
        var mail = new MailEnvelope("noreply@acme.test", "ada@acme.test", "Hello", "<p>Hi</p>", [new MailAttachment("Sales.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1, 2, 3])]);
        var key = new Dictionary<string, string?> { [MailSettings.ApiKey] = "k", [MailSettings.MailgunDomain] = "mg.acme.test" };

        var sendGrid = new FakeHttp(HttpStatusCode.Accepted, string.Empty);
        await new SendGridMailTransport(sendGrid).SendAsync(mail, new MailConnection(MailProviders.SendGrid, key), Ct);
        var attachment = JsonDocument.Parse(sendGrid.Body).RootElement.GetProperty("attachments")[0];
        attachment.GetProperty("filename").GetString().ShouldBe("Sales.xlsx");
        attachment.GetProperty("content").GetString().ShouldBe(Convert.ToBase64String([1, 2, 3]));

        var mailchimp = new FakeHttp(HttpStatusCode.OK, """[{"email":"ada@acme.test","status":"sent"}]""");
        await new MailchimpMailTransport(mailchimp).SendAsync(mail, new MailConnection(MailProviders.Mailchimp, key), Ct);
        JsonDocument.Parse(mailchimp.Body).RootElement.GetProperty("message").GetProperty("attachments")[0].GetProperty("name").GetString().ShouldBe("Sales.xlsx");

        var mailgun = new FakeHttp(HttpStatusCode.OK, "{}");
        await new MailgunMailTransport(mailgun).SendAsync(mail, new MailConnection(MailProviders.Mailgun, key), Ct);
        mailgun.ContentType.ShouldBe("multipart/form-data");
        mailgun.Body.ShouldContain("filename=Sales.xlsx");
    }

    private sealed class FakeHttp(HttpStatusCode status, string response) : HttpMessageHandler, IHttpClientFactory
    {
        public string Url { get; private set; } = string.Empty;

        public string? Authorization { get; private set; }

        public string? ContentType { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.ShouldBe(HttpMethod.Post);
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
