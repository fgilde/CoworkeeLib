using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class AvatarTests(IdentityApp app) : IAsyncLifetime
{
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_own_picture_is_shown_to_colleagues_with_a_version_and_can_be_removed()
    {
        (await Admin.PutAsJsonAsync("/api/v1/identity/me/avatar", new SetAvatarRequest(Png), Ct)).EnsureSuccessStatusCode();

        var card = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users/cards", new IdListRequest([_setup.AdminUserId]), Ct))
            .Content.ReadFromJsonAsync<List<UserCardDto>>(Ct))!.Single();
        (card.Name, card.AvatarVersion is not null).ShouldBe(("Ada Admin", true));
        var picture = await Admin.GetAsync($"/api/v1/identity/users/{_setup.AdminUserId}/avatar?v={card.AvatarVersion}", Ct);
        picture.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await app.As(_setup.AdminUserId, Guid.NewGuid()).GetAsync($"/api/v1/identity/users/{_setup.AdminUserId}/avatar", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Admin.PutAsJsonAsync("/api/v1/identity/me/avatar", new SetAvatarRequest(null), Ct)).EnsureSuccessStatusCode();
        (await Admin.GetAsync($"/api/v1/identity/users/{_setup.AdminUserId}/avatar", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_small_images_are_accepted()
    {
        (await Admin.PutAsJsonAsync("/api/v1/identity/me/avatar", new SetAvatarRequest("data:text/html;base64,PGI+"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PutAsJsonAsync("/api/v1/identity/me/avatar", new SetAvatarRequest("data:image/png;base64," + new string('A', 400_000)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
