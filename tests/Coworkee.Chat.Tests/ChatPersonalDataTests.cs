using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Privacy;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat.Tests;

public sealed class ChatPersonalDataTests(ChatApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;
    private Guid _group;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await app.CreateUserAsync(_setup, "bob@acme.test", ChatPermissions.Use);
        await InDbAsync(async db =>
        {
            var tenant = _setup.TenantId;
            var now = DateTimeOffset.UtcNow;
            var group = new ChatConversation { TenantId = tenant, IsGroup = true, Name = "Planning", CreatedBy = _bob.Id, CreatedAt = now };
            var message = new ChatMessage { TenantId = tenant, ConversationId = group.Id, FromUserId = _setup.AdminUserId, Text = "Agenda", SentAt = now };
            db.AddRange(
                group,
                new ChatMember { TenantId = tenant, ConversationId = group.Id, UserId = _bob.Id, IsOwner = true, JoinedAt = now },
                new ChatMember { TenantId = tenant, ConversationId = group.Id, UserId = _setup.AdminUserId, JoinedAt = now },
                message,
                new ChatReaction { TenantId = tenant, MessageId = message.Id, UserId = _bob.Id, Emoji = "👍" },
                new ChatAttachment { TenantId = tenant, ConversationId = group.Id, UploadedBy = _bob.Id, Name = "plan.pdf", ContentType = "application/pdf", Size = 7, BlobKey = "none", UploadedAt = now });
            await db.SaveChangesAsync(Ct);
            _group = group.Id;
            return 0;
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_export_lists_his_memberships_reactions_and_uploaded_files()
    {
        var chat = (await app.As(_bob.Id, _setup.TenantId).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct)).GetProperty("chat");

        var membership = chat.GetProperty("memberships").EnumerateArray().Single();
        (membership.GetProperty("name").GetString(), membership.GetProperty("isOwner").GetBoolean()).ShouldBe(("Planning", true));
        chat.GetProperty("reactions").EnumerateArray().Single().GetProperty("emoji").GetString().ShouldBe("👍");
        chat.GetProperty("attachments").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("plan.pdf");
    }

    [Fact]
    public async Task Erasing_keeps_his_group_for_the_others_with_the_tombstone_as_creator()
    {
        (await app.As(_setup.AdminUserId, _setup.TenantId).DeleteAsync($"/api/v1/identity/users/{_bob.Id}", Ct)).EnsureSuccessStatusCode();

        var group = await InDbAsync(db => db.Set<ChatConversation>().SingleAsync(c => c.Id == _group, Ct));
        group.CreatedBy.ShouldBe(PersonalDataSubject.ErasedUserId);
        (await InDbAsync(db => db.Set<ChatMember>().Select(m => m.UserId).ToListAsync(Ct))).ShouldBe([_setup.AdminUserId]);
        (await InDbAsync(db => db.Set<ChatMessage>().Select(m => m.Text).ToListAsync(Ct))).ShouldBe(["Agenda"]);
        (await InDbAsync(db => db.Set<ChatReaction>().CountAsync(Ct))).ShouldBe(0);
    }

    private async Task<T> InDbAsync<T>(Func<ChatTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ChatTestDbContext>());
    }
}
