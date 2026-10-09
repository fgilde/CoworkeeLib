using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.About;
using Coworkee.Client.Blazor.Customization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class AboutTests : ClientTestBase
{
    private readonly CoworkeeClientOptions _options = new() { AppTitle = "Demo App", AppVersion = "1.2.3" };

    public AboutTests() => Services.AddSingleton(_options);

    private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync()
    {
        var provider = Render<MudDialogProvider>();
        await provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowAsync<AboutDialog>());
        return provider;
    }

    [Fact]
    public async Task The_default_sections_show_app_credits_and_runtime()
    {
        _options.AboutLinks.Add(new AboutLink("Docs", "https://example.org/docs"));

        var about = await ShowAsync();

        about.WaitForAssertion(() => about.Find("[data-testid='app-version']").TextContent.ShouldBe("1.2.3"));
        about.Find("[data-testid='about-monogram']").TextContent.ShouldBe("DA");
        about.FindAll("[data-credit]").Select(c => c.GetAttribute("data-credit")).ShouldBe(["Coworkee", "Nextended", "MudBlazor.Extensions"]);
        about.Find("[data-credit='MudBlazor.Extensions'] .cw-about-credit-version").TextContent.ShouldBe(AboutVersion.Of(typeof(MudBlazor.Extensions.Options.DialogOptionsEx).Assembly));
        about.Find("[data-testid='about-links'] a").GetAttribute("href").ShouldBe("https://example.org/docs");
        about.Find("[data-testid='about-runtime']").TextContent.ShouldStartWith(".NET");
        about.Find("[data-testid='gilde']").GetAttribute("href").ShouldBe("https://gilde.org");
    }

    [Fact]
    public async Task Sections_and_credits_can_be_removed_and_added()
    {
        _options.About.Sections.Remove(typeof(AboutFooter));
        _options.About.Sections.Insert(1, typeof(LicenseSection));
        _options.About.Credits.RemoveAll(c => c.Title == "Nextended");
        _options.About.Credits.Add(new AboutCredit("My lib", "https://example.org", Icons.Material.Outlined.Extension, "4.5.6"));
        _options.About.Credits.Add(new AboutCredit("Unversioned", "https://example.org/u", Icons.Material.Outlined.Extension));

        var about = await ShowAsync();

        about.WaitForAssertion(() => about.Find("[data-testid='license']").TextContent.ShouldBe("MIT"));
        about.FindAll("[data-testid='about-footer']").ShouldBeEmpty();
        about.FindAll("[data-credit]").Select(c => c.GetAttribute("data-credit")).ShouldBe(["Coworkee", "MudBlazor.Extensions", "My lib", "Unversioned"]);
        about.Find("[data-credit='My lib'] .cw-about-credit-version").TextContent.ShouldBe("4.5.6");
        about.FindAll("[data-credit='Unversioned'] .cw-about-credit-version").ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_credits_the_section_renders_nothing()
    {
        _options.About.Credits.Clear();

        var about = await ShowAsync();

        about.WaitForAssertion(() => about.Find("[data-testid='about-header']"));
        about.FindAll("[data-testid='about-credits']").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_whole_dialog_can_be_replaced()
    {
        Services.ReplaceComponent<AboutDialog, PlainAbout>();

        var about = await ShowAsync();

        about.WaitForAssertion(() => about.Find("[data-testid='plain-about']").TextContent.ShouldBe("Demo App"));
        about.FindAll("[data-testid='about-header']").ShouldBeEmpty();
    }

    [Fact]
    public void Versions_drop_the_build_metadata() =>
        AboutVersion.Of(typeof(AboutTests).Assembly)!.ShouldNotContain('+');

    [Fact]
    public void The_highest_stated_version_wins_over_a_stale_informational_one()
    {
        var mudEx = typeof(MudBlazor.Extensions.Options.DialogOptionsEx).Assembly;

        AboutVersion.Of(mudEx).ShouldBe(mudEx.GetName().Version!.ToString(3));
    }

    public sealed class LicenseSection : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "data-testid", "license");
            builder.AddContent(2, "MIT");
            builder.CloseElement();
        }
    }

    public sealed class PlainAbout : ComponentBase
    {
        [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudDialog>(0);
            builder.AddComponentParameter(1, nameof(MudDialog.DialogContent), (RenderFragment)(content =>
            {
                content.OpenElement(0, "p");
                content.AddAttribute(1, "data-testid", "plain-about");
                content.AddContent(2, Options.AppTitle);
                content.CloseElement();
            }));
            builder.CloseComponent();
        }
    }
}
