using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Ai;

/// <summary>Lists the tools the current user may use and runs them as that user, auditing every call.</summary>
public sealed class AiToolRunner(
    AiToolCatalog catalog,
    IPermissionChecker permissions,
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

    /// <summary>The tools of the current user: none without <see cref="AiPermissions.Chat"/>. The assistant checks on its own whether it is switched on; MCP needs no model.</summary>
    public async Task<IReadOnlyList<AiTool>> AvailableAsync(CancellationToken cancellationToken)
    {
        if (_available is not null)
        {
            return _available;
        }

        var available = new List<AiTool>();
        if (currentUser.IsAuthenticated && await permissions.IsGrantedAsync(AiPermissions.Chat, cancellationToken))
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
                ApiKeyId = currentUser.ApiKeyId,
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

            if (!result.GetType().IsGenericType)
            {
                return ("Done.", null);
            }

            // the same shape as the API answers: response filters hide here what they hide there
            var value = result.GetType().GetProperty(nameof(Result<object>.Value))!.GetValue(result);
            var shaped = value is null ? null : await ResponseFilters.ResponseFilterApplier.ApplyAsync(scope.ServiceProvider, value, JsonOptions, cancellationToken);
            return (JsonSerializer.Serialize(shaped, JsonOptions), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "AI tool {Tool} failed", tool.Name);
            return ("The tool failed unexpectedly.", "The tool failed unexpectedly.");
        }
    }

    private static string Truncate(string value, int length) => value.Length > length ? value[..length] : value;
}
