using System.Net;
using System.Text;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Files;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The message of a refused call names the rule: MudExFileManager showed "ApiException: validation" for every refused upload.</summary>
public sealed class ApiExceptionTests
{
    [Fact]
    public void Message_is_the_validation_messages_first()
    {
        var errors = new Dictionary<string, string[]> { ["Name"] = ["Enter a name."], ["content"] = ["The file is larger than 10 MB.", "Enter a name."] };

        new ApiException(400, "validation", errors, "Enter a name.").Message.ShouldBe("Enter a name. The file is larger than 10 MB.");
    }

    [Theory]
    [InlineData(409, "identity.role_name_fixed", "Code finds this role by its name.", "Code finds this role by its name.")]
    [InlineData(403, "auth.forbidden", null, "auth.forbidden")]
    [InlineData(413, null, " ", "Request failed with status 413.")]
    public void Without_validation_messages_the_title_then_the_code_then_the_status(int status, string? code, string? title, string expected) =>
        new ApiException(status, code, new Dictionary<string, string[]> { ["x"] = [""] }, title).Message.ShouldBe(expected);

    [Fact]
    public async Task A_refused_upload_says_why()
    {
        const string problem = """
            {"title":"Could not complete the security scan on the uploaded file.","status":400,
             "errors":{"content":["Could not complete the security scan on the uploaded file."]},"code":"validation"}
            """;
        var api = new FilesApi(new HttpClient(new Answer(problem)) { BaseAddress = new Uri("http://test/") });

        var refused = await Should.ThrowAsync<ApiException>(() => api.UploadAsync(null, "clip.mp4", "video/mp4", new MemoryStream([1, 2, 3])));

        refused.Code.ShouldBe("validation");
        refused.Message.ShouldBe("Could not complete the security scan on the uploaded file.");
        SnackbarApiExtensions.Describe(refused).ShouldBe(refused.Message);
    }

    private sealed class Answer(string problem) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(problem, Encoding.UTF8, "application/problem+json") });
    }
}
