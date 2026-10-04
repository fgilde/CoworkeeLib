using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
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

public sealed class AiApp : PostgresFixture
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
            ["Coworkee:Settings:Defaults:Ai.Enabled"] = "true",
            ["Coworkee:Settings:Defaults:Ai.ApiKey"] = "test-key",
        });
        builder.AddCoworkee<TestAiModule>();
        builder.Services.AddTestAuthentication();
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

    public void ToolUse(params (string Name, JsonObject Input)[] uses) =>
        Enqueue("tool_use", [.. uses.Select((u, i) => new JsonObject
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
        Requests.Enqueue(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
        return _responses.TryDequeue(out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"type":"error","error":{"type":"api_error","message":"no answer queued"}}""") };
    }
}

public sealed class AiTestDbContext(DbContextOptions<AiTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeAiModule), typeof(CoworkeeIdentityModule))]
public sealed class TestAiModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<AiTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
        context.Services.AddCoworkeeOutboxProcessing<AiTestDbContext>();
        context.Services.AddMessagingFromAssembly(typeof(TestAiModule).Assembly);
        context.Services.AddSingleton<IModelContributor, NoteModel>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, NotePermissions>();
        context.Services.AddAiTool<AddNote>("add_note", "Adds a note.");
        context.Services.AddAiTool<FailNote>("fail_note", "Tries to add a note and fails.");
        context.Services.AddAiTool<ListNotes>("list_notes", "Lists the notes.");
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

public sealed record ListNotes : IQuery<Result<IReadOnlyList<string>>>;

internal sealed class NoteHandlers(CoworkeeDbContext db, ICurrentUser currentUser)
    : IHandler<AddNote, Result<Guid>>, IHandler<FailNote, Result<Guid>>, IHandler<ListNotes, Result<IReadOnlyList<string>>>
{
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
        Result<IReadOnlyList<string>>.Success(await db.Set<Note>().Select(n => n.Title).ToListAsync(cancellationToken));
}
