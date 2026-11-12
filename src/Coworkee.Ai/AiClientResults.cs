using System.Collections.Concurrent;
using System.Text.Json;
using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

/// <summary>
/// The reads the assistant handed to the browser (<see cref="AiStreamEvent.ClientQuery"/>) and wait for its answer, by user, conversation and call:
/// only the user whose turn asked can answer, and only while the turn waits.
/// </summary>
// ponytail: per process like AiChat's guard; scaled out the answer must reach the instance that streams the turn (sticky sessions), else the server answers after the timeout
public sealed class AiClientResults
{
    /// <summary>The largest answer taken; larger ones are refused and the server runs the tool.</summary>
    public const int MaxOutputLength = 200_000;

    public const int MaxCallIdLength = 200;

    private readonly ConcurrentDictionary<(Guid User, Guid Session, string CallId), TaskCompletionSource<string?>> _waiting = new();

    /// <summary>Runs <paramref name="ask"/> and waits for the browser's answer; null when it cannot answer or does not in <paramref name="timeout"/>.</summary>
    public async Task<string?> AskAsync(Guid user, Guid session, string callId, Func<Task> ask, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var key = (user, session, callId);
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_waiting.TryAdd(key, answer))
        {
            return null;
        }

        try
        {
            await ask();
            return await answer.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _waiting.TryRemove(key, out _);
        }
    }

    /// <summary>Hands the browser's answer to the waiting turn of <paramref name="user"/>; false when none waits for it.</summary>
    public bool Complete(Guid user, Guid session, AiClientResult result) =>
        _waiting.TryRemove((user, session, result.CallId), out var answer) && answer.TrySetResult(result.Output);

    /// <summary>Why the answer is refused, or null: too long, or not the JSON object the server tool answers with.</summary>
    public static string? Invalid(AiClientResult? result)
    {
        if (result is not { CallId.Length: > 0 and <= MaxCallIdLength })
        {
            return $"Send a call id of 1 to {MaxCallIdLength} characters.";
        }

        if (result.Output is null)
        {
            return null;
        }

        if (result.Output.Length > MaxOutputLength)
        {
            return $"The output may have at most {MaxOutputLength} characters.";
        }

        try
        {
            using var output = JsonDocument.Parse(result.Output);
            return output.RootElement.ValueKind == JsonValueKind.Object ? null : "The output must be a JSON object.";
        }
        catch (JsonException)
        {
            return "The output must be a JSON object.";
        }
    }
}
