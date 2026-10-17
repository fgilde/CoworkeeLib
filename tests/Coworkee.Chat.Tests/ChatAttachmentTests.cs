using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Coworkee.Chat.Tests;

public sealed class ChatAttachmentTests(ChatApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;
    private UserDto _carol = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await app.CreateUserAsync(_setup, "bob@acme.test", ChatPermissions.Use);
        _carol = await app.CreateUserAsync(_setup, "carol@acme.test", ChatPermissions.Use);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    private HttpClient Carol => app.As(_carol.Id, _setup.TenantId);

    [Fact]
    public async Task Pictures_get_a_small_upright_version_for_the_members()
    {
        var chat = await OpenDirectAsync();
        var picture = await UploadAsync(chat, "wide.png", "image/png", Image(1200, 600, SKEncodedImageFormat.Png));
        var photo = await UploadAsync(chat, "photo.jpg", "image/jpeg", Turned(Image(1200, 600, SKEncodedImageFormat.Jpeg)));
        var text = await UploadAsync(chat, "plan.txt", "text/plain", "the plan"u8.ToArray());
        await SendAsync(chat, picture.Id, photo.Id, text.Id);

        (picture.HasThumbnail, photo.HasThumbnail, text.HasThumbnail).ShouldBe((true, true, false));
        (await ThumbnailAsync(Bob, picture.Id)).ShouldBe((480, 240));
        (await ThumbnailAsync(Bob, photo.Id)).ShouldBe((240, 480));
        (await Bob.GetAsync($"/api/v1/chat/attachments/{text.Id}/thumbnail", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Carol.GetAsync($"/api/v1/chat/attachments/{picture.Id}/thumbnail", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Bob.GetFromJsonAsync<List<ChatMessageDto>>($"/api/v1/chat/chats/{chat}/messages", Ct))!.Single().Attachments
            .Select(a => a.HasThumbnail).ShouldBe([true, true, false]);
    }

    [Fact]
    public async Task Files_never_sent_are_deleted_after_their_lifetime()
    {
        var chat = await OpenDirectAsync();
        var old = await UploadAsync(chat, "old.png", "image/png", Image(10, 10, SKEncodedImageFormat.Png));
        var fresh = await UploadAsync(chat, "fresh.txt", "text/plain", "fresh"u8.ToArray());
        var sent = await UploadAsync(chat, "sent.txt", "text/plain", "sent"u8.ToArray());
        await SendAsync(chat, sent.Id);
        var keys = await InDbAsync(async db =>
        {
            await db.Set<ChatAttachment>().IgnoreQueryFilters().Where(a => a.Id == old.Id || a.Id == sent.Id)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.UploadedAt, DateTimeOffset.UtcNow.AddDays(-2)), Ct);
            return await db.Set<ChatAttachment>().IgnoreQueryFilters().Where(a => a.Id == old.Id).Select(a => new[] { a.BlobKey, a.ThumbnailKey! }).SingleAsync(Ct);
        });

        await InDbAsync(async db =>
        {
            await ActivatorUtilities.CreateInstance<ChatAttachmentCleanup>(app.App.Services, db).ExecuteAsync(Ct);
            return 0;
        });

        (await InDbAsync(db => db.Set<ChatAttachment>().IgnoreQueryFilters().Select(a => a.Id).ToListAsync(Ct))).ShouldBe([fresh.Id, sent.Id], ignoreOrder: true);
        var storage = app.App.Services.GetRequiredService<IBlobStorage>();
        foreach (var key in keys)
        {
            (await storage.ExistsAsync(key, Ct)).ShouldBeFalse();
        }

        (await Admin.GetAsync($"/api/v1/chat/attachments/{fresh.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<T> InDbAsync<T>(Func<ChatTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ChatTestDbContext>());
    }

    private async Task<Guid> OpenDirectAsync() =>
        (await (await Admin.PostAsync($"/api/v1/chat/chats/direct/{_bob.Id}", null, Ct)).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ChatConversationDto>(Ct))!.Id;

    private async Task SendAsync(Guid chat, params Guid[] attachments) =>
        (await Admin.PostAsJsonAsync($"/api/v1/chat/chats/{chat}/messages", new PostChatMessageRequest("", null, attachments), Ct)).EnsureSuccessStatusCode();

    private async Task<ChatAttachmentDto> UploadAsync(Guid chat, string name, string type, byte[] content)
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(type);
        return (await (await Admin.PostAsync($"/api/v1/chat/chats/{chat}/attachments?name={name}", body, Ct)).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<ChatAttachmentDto>(Ct))!;
    }

    private static async Task<(int Width, int Height)> ThumbnailAsync(HttpClient client, Guid id)
    {
        var response = (await client.GetAsync($"/api/v1/chat/attachments/{id}/thumbnail", Ct)).EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync(Ct));
        return (bitmap.Width, bitmap.Height);
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 90).ToArray();
    }

    // an EXIF block right after the start marker: orientation 6, the camera was turned by 90 degrees
    private static byte[] Turned(byte[] jpeg)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }
}
