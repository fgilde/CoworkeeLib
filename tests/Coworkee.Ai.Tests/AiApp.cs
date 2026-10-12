using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Authentication;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Ai.Tests.AiApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Ai.Tests;

public sealed class AiApp : DatabaseFixture
{
    public WebApplication App { get; private set; } = null!;

    public FakeClaude Claude { get; } = new();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:SetupToken"] = "token",
            ["ConnectionStrings:test"] = ConnectionString,
            ["Coworkee:Database:Provider"] = Provider,
            ["Coworkee:Settings:Defaults:Ai.Enabled"] = "true",
            ["Coworkee:Settings:Defaults:Ai.ApiKey"] = "test-key",
        });
        builder.AddCoworkee<TestAiModule>();
        builder.Services.AddTestAuthentication();
        builder.Services.AddCoworkeeApiKeys();
        builder.Services.AddHttpClient(AiChat.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Claude);
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AiTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();
        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<SetupResultDto> SetupAsync()
    {
        await ResetAsync();
        Claude.Reset();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    /// <summary>A new user of the admin's organisation with a new role granting <paramref name="permissions"/>.</summary>
    public async Task<Guid> UserAsync(HttpClient admin, params string[] permissions)
    {
        var ct = TestContext.Current.CancellationToken;
        var user = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", "Some", "One"), ct))
            .Content.ReadFromJsonAsync<UserDto>(ct))!.Id;
        if (permissions.Length > 0)
        {
            await SetRolesAsync(admin, user, await RoleAsync(admin, permissions));
        }

        return user;
    }

    public static async Task<Guid> RoleAsync(HttpClient admin, params string[] permissions)
    {
        var ct = TestContext.Current.CancellationToken;
        var role = (await (await admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Role " + Guid.NewGuid().ToString("N")[..6], null), ct)).Content.ReadFromJsonAsync<Guid>(ct))!;
        (await admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), ct)).EnsureSuccessStatusCode();
        return role;
    }

    public static async Task SetRolesAsync(HttpClient admin, Guid user, params Guid[] roles) =>
        (await admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest(roles), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    /// <summary>A new session of the client's user.</summary>
    public static async Task<Guid> NewSessionAsync(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/v1/ai/sessions", new AiSessionRequest(null), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AiSessionDto>(TestContext.Current.CancellationToken))!.Id;

    /// <summary>Sends a message (or a decision) and reads the streamed answer to its end.</summary>
    public static async Task<IReadOnlyList<AiStreamEvent>> StreamAsync(HttpClient client, Guid session, object body, string path = "messages")
    {
        var response = await client.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/{path}", body, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-ndjson");
        var lines = (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return [.. lines.Select(l => System.Text.Json.JsonSerializer.Deserialize<AiStreamEvent>(l, System.Text.Json.JsonSerializerOptions.Web)!)];
    }

    public static Task<IReadOnlyList<AiStreamEvent>> AskAsync(HttpClient client, Guid session, string text) => StreamAsync(client, session, new AiSendRequest(text));

    public async Task<T> InDbAsync<T>(Func<AiTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AiTestDbContext>());
    }
}

/// <summary>Stands in for the Claude API: answers with queued messages and keeps the request bodies.</summary>
public sealed class FakeClaude : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _responses = new();

    public ConcurrentQueue<JsonObject> Requests { get; } = new();

    public void Reset()
    {
        _responses.Clear();
        Requests.Clear();
    }

    public void Text(string text) => Enqueue("end_turn", new JsonObject { ["type"] = "text", ["text"] = text });

    public void ToolUse(params (string Name, JsonObject Input)[] uses) => ToolUseStopping("tool_use", uses);

    public void ToolUseStopping(string stopReason, params (string Name, JsonObject Input)[] uses) =>
        Enqueue(stopReason, [.. uses.Select((u, i) => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = $"toolu_{Guid.NewGuid():N}",
            ["name"] = u.Name,
            ["input"] = u.Input,
            ["caller"] = new JsonObject { ["type"] = "direct" },
        })]);

    private void Enqueue(string stopReason, params JsonNode[] content) =>
        _responses.Enqueue(new JsonObject
        {
            ["id"] = $"msg_{Guid.NewGuid():N}",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = AiSettings.DefaultModel,
            ["content"] = new JsonArray(content),
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
        }.ToJsonString());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sent = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        Requests.Enqueue(sent);
        if (!_responses.TryDequeue(out var body))
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"type":"error","error":{"type":"api_error","message":"no answer queued"}}""") };
        }

        return sent["stream"]?.GetValue<bool>() == true
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ServerSentEvents(JsonNode.Parse(body)!.AsObject()), Encoding.UTF8, "text/event-stream") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    // the message as the streaming Messages API sends it: text in two deltas, tool input as JSON deltas
    private static string ServerSentEvents(JsonObject message)
    {
        var events = new StringBuilder();
        void Event(string type, JsonObject data)
        {
            data["type"] = type;
            events.Append("event: ").Append(type).Append("\ndata: ").Append(data.ToJsonString()).Append("\n\n");
        }

        var content = message["content"]!.AsArray().Select(c => c!.DeepClone().AsObject()).ToList();
        var start = message.DeepClone().AsObject();
        start["content"] = new JsonArray();
        start["stop_reason"] = null;
        Event("message_start", new JsonObject { ["message"] = start });
        for (var index = 0; index < content.Count; index++)
        {
            var block = content[index];
            if (block["type"]!.GetValue<string>() == "text")
            {
                var text = block["text"]!.GetValue<string>();
                Event("content_block_start", new JsonObject { ["index"] = index, ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = "" } });
                foreach (var part in new[] { text[..(text.Length / 2)], text[(text.Length / 2)..] })
                {
                    Event("content_block_delta", new JsonObject { ["index"] = index, ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = part } });
                }
            }
            else
            {
                var input = block["input"]!.ToJsonString();
                block["input"] = new JsonObject();
                Event("content_block_start", new JsonObject { ["index"] = index, ["content_block"] = block });
                Event("content_block_delta", new JsonObject { ["index"] = index, ["delta"] = new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = input } });
            }

            Event("content_block_stop", new JsonObject { ["index"] = index });
        }

        Event("message_delta", new JsonObject
        {
            ["delta"] = new JsonObject { ["stop_reason"] = message["stop_reason"]!.DeepClone(), ["stop_sequence"] = null },
            ["usage"] = new JsonObject { ["output_tokens"] = 1 },
        });
        Event("message_stop", []);
        return events.ToString();
    }
}

public sealed class AiTestDbContext(DbContextOptions<AiTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeAiModule), typeof(CoworkeeIdentityModule), typeof(Coworkee.OData.CoworkeeODataModule))]
public sealed class TestAiModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<AiTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddCoworkeeOutboxProcessing<AiTestDbContext>();
        context.Services.AddMessagingFromAssembly(typeof(TestAiModule).Assembly);
        context.Services.AddSingleton<IModelContributor, NoteModel>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, NotePermissions>();
        context.Services.AddAiTool<AddNote>("add_note", "Adds a note.");
        context.Services.AddAiTool<FailNote>("fail_note", "Tries to add a note and fails.");
        context.Services.AddAiTool<ListNotes>("list_notes", "Lists the notes.");
        context.Services.AddAiTool<DeleteNote>("delete_note", "Deletes a note by title.");
    }
}

[NotAudited]
public sealed class Note : AggregateRoot
{
    public required string Title { get; set; }

    public Guid? CreatedBy { get; set; }
}

internal sealed class NoteModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Note>().ToTable("Notes");
}

internal sealed class NotePermissions : IPermissionDefinitionContributor
{
    public const string Write = "Notes.Write";

    public void Define(PermissionDefinitionContext context) => context.Group("Notes", "Notes").Add(Write, "Write notes");
}

/// <summary>A note to add.</summary>
[RequiresPermission(NotePermissions.Write)]
public sealed record AddNote(string Title) : ICommand<Result<Guid>>;

[RequiresPermission(NotePermissions.Write)]
public sealed record FailNote(string Title) : ICommand<Result<Guid>>;

public sealed record ListNotes(string? Contains) : IQuery<Result<IReadOnlyList<string>>>;

[RequiresPermission(NotePermissions.Write)]
public sealed record DeleteNote(string Title) : ICommand<Result>;

internal sealed class NoteHandlers(CoworkeeDbContext db, ICurrentUser currentUser)
    : IHandler<AddNote, Result<Guid>>, IHandler<FailNote, Result<Guid>>, IHandler<ListNotes, Result<IReadOnlyList<string>>>, IHandler<DeleteNote, Result>
{
    public async Task<Result> HandleAsync(DeleteNote command, CancellationToken cancellationToken)
    {
        db.RemoveRange(await db.Set<Note>().Where(n => n.Title == command.Title).ToListAsync(cancellationToken));
        return Result.Success();
    }

    public Task<Result<Guid>> HandleAsync(AddNote command, CancellationToken cancellationToken)
    {
        var note = new Note { Title = command.Title, CreatedBy = currentUser.UserId };
        db.Add(note);
        return Task.FromResult<Result<Guid>>(note.Id);
    }

    public Task<Result<Guid>> HandleAsync(FailNote command, CancellationToken cancellationToken)
    {
        db.Add(new Note { Title = command.Title, CreatedBy = currentUser.UserId });
        return Task.FromResult<Result<Guid>>(Error.Validation(nameof(command.Title), "Notes like this are not allowed."));
    }

    public async Task<Result<IReadOnlyList<string>>> HandleAsync(ListNotes query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<string>>.Success(await db.Set<Note>().Where(n => query.Contains == null || n.Title.Contains(query.Contains)).Select(n => n.Title).ToListAsync(cancellationToken));
}
