using System.Net.Http.Headers;
using System.Net;
using System.Text.RegularExpressions;

namespace Coworkee.AuthServer.Tests;

/// <summary>Walks the registration wizard like a browser without script: every post carries the step, the encrypted state and the token.</summary>
internal sealed partial class RegistrationWizard(HttpClient browser)
{
    public string Html { get; private set; } = string.Empty;

    public string Step => StepField().Match(Html).Groups[1].Value;

    public IReadOnlyList<string> Errors => [.. Error().Matches(Html).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))];

    public async Task<RegistrationWizard> StartAsync()
    {
        Html = await browser.GetStringAsync("/Account/Register", TestContext.Current.CancellationToken);
        return this;
    }

    public Task<RegistrationWizard> NextAsync(params (string Name, string Value)[] fields) => PostAsync("next", fields, []);

    public Task<RegistrationWizard> BackAsync() => PostAsync("back", [], []);

    public Task<RegistrationWizard> SubmitAsync(params (string Field, string FileName, string ContentType, int Size)[] files) => PostAsync("next", [], files);

    /// <summary>Account, profile with address and the given roles; stops on the last step.</summary>
    public async Task<RegistrationWizard> FillAsync(string email, params Guid[] roles)
    {
        await StartAsync();
        await NextAsync(("Input.Email", email), ("Input.Password", "Passw0rd!x"), ("Input.ConfirmPassword", "Passw0rd!x"));
        await NextAsync(("Input.FirstName", "Nia"), ("Input.LastName", "New"), ("Input.PhoneNumber", "+49 1"), ("Input.Street", "Main 1"),
            ("Input.ZipCode", "12345"), ("Input.City", "Town"), ("Input.Country", "Germany"));
        if (Step == "roles")
        {
            await NextAsync([.. roles.Select(r => ("Input.RoleIds", r.ToString()))]);
        }

        return this;
    }

    private async Task<RegistrationWizard> PostAsync(string nav, (string Name, string Value)[] fields, (string Field, string FileName, string ContentType, int Size)[] files)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(Token().Match(Html).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent(Step), "Step" },
            { new StringContent(StateField().Match(Html).Groups[1].Value), "State" },
            { new StringContent(nav), "nav" },
        };
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }

        foreach (var (field, fileName, contentType, size) in files)
        {
            var content = new ByteArrayContent(new byte[size]);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, field, fileName);
        }

        using var response = await browser.PostAsync("/Account/Register", form, TestContext.Current.CancellationToken);
        Html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return this;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Token();

    [GeneratedRegex("name=\"Step\" value=\"([^\"]*)\"")]
    private static partial Regex StepField();

    [GeneratedRegex("name=\"State\" value=\"([^\"]*)\"")]
    private static partial Regex StateField();

    [GeneratedRegex("<p class=\"error\" role=\"alert\">([^<]*)</p>")]
    private static partial Regex Error();
}
