using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class AppConfigurationEditorTests : ClientTestBase
{
    private static readonly ClientAppConfiguration Registration = new("Sample", "Sample", typeof(SampleConfig), null);

    public AppConfigurationEditorTests()
    {
        Api.GetAppConfigurationAsync("Sample", Arg.Any<CancellationToken>()).Returns(Values("Default", 10, []));
        Api.SaveAppConfigurationAsync("Sample", Arg.Any<JsonElement>(), Arg.Any<CancellationToken>()).Returns(Values("Changed", 10, ["Sample:Name"]));
    }

    private static AppConfigurationValuesDto Values(string name, int limit, IReadOnlyList<string> changed) => new(
        "Sample",
        JsonSerializer.SerializeToElement(new SampleConfig { Name = name, Limit = limit }, JsonSerializerOptions.Web),
        JsonSerializer.SerializeToElement(new SampleConfig { Name = "Default", Limit = 10 }, JsonSerializerOptions.Web),
        changed);

    [Fact]
    public void Edits_the_typed_section_and_saves_it()
    {
        var editor = Render<AppConfigurationEditor<SampleConfig>>(p => p.Add(e => e.Registration, Registration));

        editor.WaitForAssertion(() => editor.FindAll("input").Select(i => i.GetAttribute("value")).ShouldContain("Default"));
        editor.Find("[data-testid='reset']").HasAttribute("disabled").ShouldBeTrue();

        editor.InvokeAsync(() => editor.Instance.SaveAsync()).GetAwaiter().GetResult();

        Api.Received(1).SaveAppConfigurationAsync("Sample", Arg.Is<JsonElement>(v => v.GetProperty("name").GetString() == "Default" && v.GetProperty("limit").GetInt32() == 10), Arg.Any<CancellationToken>());
        editor.WaitForAssertion(() => editor.Find("[data-testid='changed-count']").TextContent.ShouldContain("1 changed"));
    }

    [Fact]
    public void The_page_shows_a_tab_per_registered_section()
    {
        Services.AddSingleton(Registration);
        Services.AddSingleton(new ClientAppConfiguration("Other", "Other things", typeof(SampleConfig), null));
        Authorize();

        var page = Render<AppConfiguration>();

        page.Markup.ShouldContain("Other things");
        page.FindAll("[data-section]").Select(e => e.GetAttribute("data-section")).ShouldContain("Sample");
    }

    private void Authorize()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies("perm:" + SettingsPermissions.Manage);
    }

    public sealed class SampleConfig
    {
        public string? Name { get; set; }

        public int Limit { get; set; }
    }
}
