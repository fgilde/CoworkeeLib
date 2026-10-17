using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Theming;
using Coworkee.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Theming.Tests;

public sealed class ThemeTests(ThemeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static ThemeRequest Brand(string name = "Brand", string primary = "#123456", string? css = null, string? logo = null) =>
        new(name, Palette(primary), Palette("#abcdef"), null, null, logo, css);

    private static JsonElement Palette(string primary) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Primary"] = primary, ["Secondary"] = "rgba(10, 20, 30, 0.5)", ["HoverOpacity"] = 0.06 });

    private static readonly string[] BuiltIn =
        ["Arctic", "Atom", "Aubergine", "Classic", "Code", "Coworkee", "Forest", "High Contrast", "Midnight", "Ocean", "Octo", "Paper", "Solar", "Sunset", "Vampire"];

    [Fact]
    public async Task Seeds_global_themes_with_ocean_as_default()
    {
        var themes = await Admin.GetFromJsonAsync<ThemeDto[]>("/api/v1/themes", Ct);

        themes!.Where(t => t.IsGlobal).Select(t => t.Name).ShouldBe(BuiltIn, ignoreOrder: true);
        (await app.Anonymous().GetFromJsonAsync<ThemeDto>("/api/v1/themes/current", Ct))!.Name.ShouldBe("Ocean");
    }

    [Fact]
    public async Task Reading_themes_does_not_rewrite_unchanged_built_in_themes()
    {
        await app.InDbAsync(async db =>
        {
            await ThemeSeeds.EnsureAsync(db, new ThemingOptions(), Ct);
            return 0;
        });
        var before = await app.InDbAsync(db => db.Set<ThemeDefinition>().Where(t => t.TenantId == null).Select(t => t.ModifiedAt).ToListAsync(Ct));

        await app.InDbAsync(async db =>
        {
            await ThemeSeeds.EnsureAsync(db, new ThemingOptions(), Ct);
            return 0;
        });

        (await app.InDbAsync(db => db.Set<ThemeDefinition>().Where(t => t.TenantId == null).Select(t => t.ModifiedAt).ToListAsync(Ct))).ShouldBe(before);
    }

    [Fact]
    public async Task Built_in_themes_seeded_by_requests_at_the_same_time_are_listed_once()
    {
        await app.InDbAsync(db => db.Set<ThemeDefinition>().Where(t => t.TenantId == null && !db.Set<TenantTheme>().Any(m => m.ThemeId == t.Id)).ExecuteDeleteAsync(Ct));

        var lists = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => app.Anonymous().GetFromJsonAsync<ThemeDto[]>("/api/v1/themes/built-in", Ct)));

        lists.ShouldAllBe(themes => themes!.Select(t => t.Name).Order().SequenceEqual(BuiltIn));
    }

    [Fact]
    public async Task A_built_in_theme_cannot_be_stored_twice()
    {
        await Should.ThrowAsync<DbUpdateException>(() => app.InDbAsync(async db =>
        {
            db.AddRange(ThemeSeeds.Create(new ThemingOptions()).Single(t => t.Name == "Octo"), ThemeSeeds.Create(new ThemingOptions()).Single(t => t.Name == "Octo"));
            return await db.SaveChangesAsync(Ct);
        }));
    }

    [Fact]
    public async Task Built_in_themes_are_listed_for_anyone_so_setup_can_offer_them()
    {
        var themes = await app.Anonymous().GetFromJsonAsync<ThemeDto[]>("/api/v1/themes/built-in", Ct);

        themes!.Select(t => t.Name).ShouldBe(BuiltIn, ignoreOrder: true);
        themes.ShouldAllBe(t => t.IsGlobal);
        (await Admin.PostAsJsonAsync("/api/v1/themes", Brand("Private"), Ct)).EnsureSuccessStatusCode();
        (await app.Anonymous().GetFromJsonAsync<ThemeDto[]>("/api/v1/themes/built-in", Ct))!.ShouldNotContain(t => t.Name == "Private");
    }

    [Fact]
    public async Task Setup_makes_the_chosen_theme_and_mode_the_organisation_default()
    {
        _setup = await app.SetupAsync("Ocean", new Dictionary<string, string?> { ["Theme.Mode"] = "dark" });

        (await Admin.GetFromJsonAsync<ThemeDto>("/api/v1/themes/current", Ct))!.Name.ShouldBe("Ocean");
        (await app.Anonymous().GetFromJsonAsync<ThemeDto>("/api/v1/themes/current", Ct))!.Name.ShouldBe("Ocean");
        (await Admin.GetFromJsonAsync<Dictionary<string, string?>>("/api/v1/settings/client", Ct))!["Theme.Mode"].ShouldBe("dark");
    }

    [Fact]
    public async Task Setup_rejects_a_theme_that_is_not_built_in()
    {
        await app.ResetAsync();
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Coworkee.Identity.Setup.SystemStateCache>(app.App.Services).Reset();

        var response = await app.Anonymous().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", null, null, null, Guid.CreateVersion7()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _setup = await app.SetupAsync();
    }

    [Fact]
    public async Task Current_follows_the_system_tenant_default_for_anonymous_users()
    {
        var created = await (await Admin.PostAsJsonAsync("/api/v1/themes", Brand(), Ct)).Content.ReadFromJsonAsync<ThemeDto>(Ct);

        (await Admin.PostAsync($"/api/v1/themes/{created!.Id}/default", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await app.Anonymous().GetFromJsonAsync<ThemeDto>("/api/v1/themes/current", Ct))!.Name.ShouldBe("Brand");
        var (user, tenant) = await app.CreateTenantAdminAsync();
        (await app.As(user, tenant).GetFromJsonAsync<ThemeDto>("/api/v1/themes/current", Ct))!.Name.ShouldBe("Ocean");
    }

    [Theory]
    [InlineData("red;}")]
    [InlineData("expression(alert(1))")]
    [InlineData("url(javascript:alert(1))")]
    public async Task Invalid_colors_are_rejected(string color) =>
        (await Admin.PostAsJsonAsync("/api/v1/themes", Brand(primary: color), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Palette_must_be_an_object() =>
        (await Admin.PostAsJsonAsync("/api/v1/themes", Brand() with { PaletteLight = JsonSerializer.SerializeToElement(new[] { 1 }) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("</style><script>alert(1)</script>", null)]
    [InlineData("@import url(https://evil.test/x.css);", null)]
    [InlineData(null, "<svg onload=\"alert(1)\"></svg>")]
    [InlineData(null, "<svg><script>alert(1)</script></svg>")]
    [InlineData(null, "<img src=x>")]
    [InlineData(null, "<svg/onload=alert(1)></svg>")]
    [InlineData(null, "<svg a=\"b\"onload=alert(1)></svg>")]
    [InlineData(null, "<svg><a href=\"javascript&colon;alert(1)\">x</a></svg>")]
    [InlineData(null, "<svg><a href=\"&#106;avascript:alert(1)\">x</a></svg>")]
    [InlineData(null, "<svg><style>body{display:none}</style></svg>")]
    [InlineData(null, "<svg><image href=\"https://evil.test/p.gif\"/></svg>")]
    public async Task Unsafe_css_and_svg_are_rejected(string? css, string? logo) =>
        (await Admin.PostAsJsonAsync("/api/v1/themes", Brand(css: css, logo: logo), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"4\"/></svg>")]
    [InlineData("<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"4\"/></svg>")]
    [InlineData("data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciPjwvc3ZnPg==")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("data:image/jpeg;base64,/9j/4AAQ")]
    [InlineData("data:image/webp;base64,UklGRg==")]
    public async Task A_logo_is_svg_markup_or_an_image_data_url(string logo)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/themes", Brand(logo: logo), Ct);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<ThemeDto>(Ct))!.Logo.ShouldBe(logo.Trim());
    }

    [Fact]
    public async Task A_logo_may_have_at_most_512_kb()
    {
        string Png(int bytes) => "data:image/png;base64," + Convert.ToBase64String(new byte[bytes]);

        (await Admin.PostAsJsonAsync("/api/v1/themes", Brand(logo: Png(ThemeLogo.MaxBytes)), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/themes", Brand("Big", logo: Png(ThemeLogo.MaxBytes + 1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("data:image/gif;base64,R0lGODlh")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("data:image/png;charset=utf-8,abc")]
    [InlineData("data:image/png;base64,***")]
    [InlineData("https://evil.test/logo.png")]
    [InlineData("data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9ImFsZXJ0KDEpIj48L3N2Zz4=")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48c2NyaXB0PmFsZXJ0KDEpPC9zY3JpcHQ+PC9zdmc+")]
    public async Task Other_logos_and_unsafe_svg_data_urls_are_rejected(string logo) =>
        (await Admin.PostAsJsonAsync("/api/v1/themes", Brand(logo: logo), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Global_themes_are_read_only()
    {
        var coworkee = (await Admin.GetFromJsonAsync<ThemeDto[]>("/api/v1/themes", Ct))!.Single(t => t.Name == "Coworkee");

        (await Admin.PutAsJsonAsync($"/api/v1/themes/{coworkee.Id}", Brand("Hacked"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.DeleteAsync($"/api/v1/themes/{coworkee.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Theme_changes_are_versioned()
    {
        var created = await (await Admin.PostAsJsonAsync("/api/v1/themes", Brand(), Ct)).Content.ReadFromJsonAsync<ThemeDto>(Ct);

        (await Admin.PutAsJsonAsync($"/api/v1/themes/{created!.Id}", Brand("Brand 2"), Ct)).EnsureSuccessStatusCode();

        (await app.InDbAsync(db => db.Set<EntitySnapshot>().CountAsync(s => s.EntityId == created.Id.ToString(), Ct))).ShouldBe(2);
    }

    [Fact]
    public async Task Other_tenants_neither_see_nor_use_my_themes()
    {
        var created = await (await Admin.PostAsJsonAsync("/api/v1/themes", Brand(), Ct)).Content.ReadFromJsonAsync<ThemeDto>(Ct);
        var (user, tenant) = await app.CreateTenantAdminAsync();
        var other = app.As(user, tenant);

        (await other.GetFromJsonAsync<ThemeDto[]>("/api/v1/themes", Ct))!.ShouldNotContain(t => t.Id == created!.Id);
        (await other.PostAsync($"/api/v1/themes/{created!.Id}/default", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Managing_themes_requires_permission()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await app.As(bob.Id, _setup.TenantId).PostAsJsonAsync("/api/v1/themes", Brand(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
