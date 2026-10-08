using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Ai;

/// <summary>
/// A tool the assistant and MCP clients may call: a dispatcher request, so it runs with the permission, resource,
/// validation and unit of work checks of the API. The JSON input is deserialized into <see cref="RequestType"/>.
/// </summary>
public sealed record AiTool(string Name, string Description, Type RequestType, Type ResultType)
{
    public IReadOnlyList<string> Permissions { get; } = RequestType.GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission).ToList();

    /// <summary>Queries only read; commands change data and run only on a direct user request (see <see cref="AiToolRunner.RunAsync"/>).</summary>
    public bool ReadOnly { get; } = !RequestType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
}

public static class AiToolServiceCollectionExtensions
{
    /// <summary>Offers <typeparamref name="TRequest"/> as a tool. Names are lower snake case and unique; the request returns a <see cref="Result"/>.</summary>
    public static IServiceCollection AddAiTool<TRequest>(this IServiceCollection services, string name, string description)
        where TRequest : class
    {
        var resultType = typeof(TRequest).GetInterfaces()
            .SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))?.GetGenericArguments()[0]
            ?? throw new ArgumentException($"{typeof(TRequest).Name} is not a dispatcher request.", nameof(TRequest));
        if (!typeof(Result).IsAssignableFrom(resultType))
        {
            throw new ArgumentException($"{typeof(TRequest).Name} must return a Result, so failures reach the model as messages.", nameof(TRequest));
        }

        return services.AddSingleton(new AiTool(name, description, typeof(TRequest), resultType));
    }
}

public sealed record AiToolOutcome(string Output, bool Succeeded, AiToolCallDto Call);

/// <summary>Lists the tools the current user may use and runs them as that user, auditing every call.</summary>
public sealed class AiToolRunner(
    AiToolCatalog catalog,
    IPermissionChecker permissions,
    ISettingProvider settings,
    ICurrentUser currentUser,
    IServiceScopeFactory scopes,
    IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json,
    AiToolSchemas schemas,
    ILogger<AiToolRunner> logger)
{
    public const int MaxOutputLength = 20_000;

    public const string WriteNeedsUser =
        "This tool changes data and runs only right after a user message. Tell the user what you are about to do and ask them to confirm.";

    private const int MaxAuditedInputLength = 4_000;

    private static readonly MethodInfo Send = typeof(IDispatcher).GetMethod(nameof(IDispatcher.SendAsync))!;

    private IReadOnlyList<AiTool>? _available;

    public JsonSerializerOptions JsonOptions => json.Value.SerializerOptions;

    public JsonObject SchemaOf(AiTool tool) => schemas.Get(tool, JsonOptions);

    /// <summary>The tools of the current user: none without <see cref="AiPermissions.Chat"/> or while the assistant is off.</summary>
    public async Task<IReadOnlyList<AiTool>> AvailableAsync(CancellationToken cancellationToken)
    {
        if (_available is not null)
        {
            return _available;
        }

        var available = new List<AiTool>();
        if (currentUser.IsAuthenticated
            && await settings.GetAsync<bool>(AiSettings.Enabled, cancellationToken)
            && await permissions.IsGrantedAsync(AiPermissions.Chat, cancellationToken))
        {
            foreach (var tool in catalog.Tools)
            {
                var granted = true;
                foreach (var permission in tool.Permissions)
                {
                    granted &= await permissions.IsGrantedAsync(permission, cancellationToken);
                }

                if (granted)
                {
                    available.Add(tool);
                }
            }
        }

        return _available = available;
    }

    /// <summary>
    /// Runs a tool and audits the call, also when it fails or is cancelled. With <paramref name="allowWrites"/> false only
    /// read only tools run: the assistant passes false once tool output is in its context, so text planted in content
    /// cannot make it change data without the user asking again.
    /// </summary>
    public async Task<AiToolOutcome> RunAsync(string name, JsonElement input, string channel, bool allowWrites, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var output = "The call was cancelled.";
        string? error = output;
        AiToolCall call;
        try
        {
            var tool = (await AvailableAsync(cancellationToken)).FirstOrDefault(t => t.Name == name);
            if (tool is null)
            {
                error = output = $"Unknown tool '{name}'.";
            }
            else if (!tool.ReadOnly && !allowWrites)
            {
                error = output = WriteNeedsUser;
            }
            else
            {
                (output, error) = await DispatchAsync(tool, input, cancellationToken);
            }

            if (output.Length > MaxOutputLength)
            {
                output = output[..MaxOutputLength] + " …(truncated)";
            }
        }
        finally
        {
            call = new AiToolCall
            {
                TenantId = currentUser.TenantId,
                UserId = currentUser.UserId,
                Channel = channel,
                Tool = Truncate(name, 100),
                Input = Truncate(AiAuditRedaction.Redact(input), MaxAuditedInputLength),
                Succeeded = error is null,
                Error = error is null ? null : Truncate(error, 2000),
                DurationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                At = DateTimeOffset.UtcNow,
            };

            // Own scope: a failed tool may leave changes in its context that must never be saved with the audit row.
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
            db.Add(call);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return new AiToolOutcome(output, error is null, AiToolCall.ToDto(call, output));
    }

    private async Task<(string Output, string? Error)> DispatchAsync(AiTool tool, JsonElement input, CancellationToken cancellationToken)
    {
        object request;
        try
        {
            request = (input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? JsonSerializer.Deserialize("{}", tool.RequestType, JsonOptions)
                : input.Deserialize(tool.RequestType, JsonOptions)) ?? throw new JsonException("The input is empty.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = $"Invalid input: {exception.Message}";
            return (message, message);
        }

        try
        {
            // Own scope per call: a failed command must not leave tracked changes that a later call's unit of work saves.
            await using var scope = scopes.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var task = (Task)Send.MakeGenericMethod(tool.ResultType).Invoke(dispatcher, [request, cancellationToken])!;
            await task;
            var result = (Result)task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task)!;
            if (result.Error is { } failure)
            {
                var message = failure.Details is { Count: > 0 } details
                    ? $"{failure.Code}: {failure.Message} {string.Join("; ", details.Select(d => $"{d.Key}: {string.Join(" ", d.Value)}"))}"
                    : $"{failure.Code}: {failure.Message}";
                return (message, message);
            }

            return result.GetType().IsGenericType
                ? (JsonSerializer.Serialize(result.GetType().GetProperty(nameof(Result<object>.Value))!.GetValue(result), JsonOptions), null)
                : ("Done.", null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "AI tool {Tool} failed", tool.Name);
            return ("The tool failed unexpectedly.", "The tool failed unexpectedly.");
        }
    }

    private static string Truncate(string value, int length) => value.Length > length ? value[..length] : value;
}

/// <summary>Input schemas of the tools, built once from the request types.</summary>
public sealed class AiToolSchemas
{
    private readonly Dictionary<string, JsonObject> _schemas = [];

    public JsonObject Get(AiTool tool, JsonSerializerOptions options)
    {
        lock (_schemas)
        {
            if (!_schemas.TryGetValue(tool.Name, out var schema))
            {
                var resolved = new JsonSerializerOptions(options)
                {
                    TypeInfoResolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver(),
                    RespectNullableAnnotations = true,
                };
                schema = JsonSchemaExporter.GetJsonSchemaAsNode(resolved, tool.RequestType, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }) as JsonObject
                    ?? new JsonObject { ["type"] = "object" };

                // nullable inputs may be left out instead of sent as null
                if (schema["required"] is JsonArray required && schema["properties"] is JsonObject properties)
                {
                    foreach (var name in required.Select(r => r!.GetValue<string>()).ToList())
                    {
                        if (properties[name] is JsonObject property && property["type"] is JsonArray types && types.Any(t => t?.GetValue<string>() == "null"))
                        {
                            required.Remove(required.First(r => r!.GetValue<string>() == name));
                        }
                    }
                }

                _schemas[tool.Name] = schema;
            }

            return (JsonObject)schema.DeepClone();
        }
    }
}

/// <summary>Masks values of inputs that look like secrets before they are stored in the audit.</summary>
public static class AiAuditRedaction
{
    private static readonly string[] Sensitive = ["password", "secret", "apikey", "api_key", "token", "credential"];

    public static string Redact(JsonElement input)
    {
        if (input.ValueKind is JsonValueKind.Undefined)
        {
            return "{}";
        }

        var node = JsonNode.Parse(input.GetRawText());
        Mask(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void Mask(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (Sensitive.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    {
                        obj[key] = "***";
                    }
                    else
                    {
                        Mask(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Mask(item);
                }

                break;
        }
    }
}

[NotAudited]
public sealed class AiToolCall : AggregateRoot
{
    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Channel")]
    public required string Channel { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Tool")]
    public required string Tool { get; set; }

    public required string Input { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Succeeded")]
    public bool Succeeded { get; set; }

    public string? Error { get; set; }

    public int DurationMs { get; set; }

    public DateTimeOffset At { get; set; }

    public static AiToolCallDto ToDto(AiToolCall c, string? output = null) =>
        new(c.Id, c.Tool, c.Channel, c.Input, c.Succeeded, c.Error, c.DurationMs, c.At, c.UserId, output);
}

internal sealed class AiModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<AiToolCall>(call =>
        {
            call.ToTable("AiToolCalls", "cw");
            call.Property(c => c.Channel).HasMaxLength(20);
            call.Property(c => c.Tool).HasMaxLength(100);
            call.Property(c => c.Input).HasMaxLength(4000);
            call.Property(c => c.Error).HasMaxLength(2000);
            call.HasIndex(c => new { c.TenantId, c.At });
        });
}
