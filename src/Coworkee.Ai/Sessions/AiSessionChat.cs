using System.Text.Json;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai.Sessions;

/// <summary>
/// Sends messages of a session to <see cref="AiChat"/> and saves question and answer. The answer streams as it comes; it keeps running when
/// the browser goes away, so every open tab of the user sees it once saved.
/// </summary>
internal sealed class AiSessionChat(AiChat chat, CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private const int MaxHistory = 40;

    private const int MaxTitleLength = 60;

    public async Task<IResult> SendAsync(Guid sessionId, AiSendRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Text) || request.Text.Length > AiChat.MaxMessageLength)
        {
            return Error.Validation(nameof(request.Text), $"Write 1 to {AiChat.MaxMessageLength} characters.").ToProblem();
        }

        var connection = await chat.ConnectAsync(cancellationToken);
        if (connection.Error is { } unavailable)
        {
            return unavailable.ToProblem();
        }

        if (await LoadAsync(sessionId, cancellationToken) is not { } session)
        {
            return NotFound.ToProblem();
        }

        // a new question leaves an open confirmation behind
        foreach (var waiting in session.Messages.Where(m => m.Pending is not null && m.Decision is null))
        {
            waiting.Decision = AiDecisions.Declined;
        }

        var question = Add(session, "user", request.Text.Trim());
        if (session.Title == AiSessionHandlers.NewTitle)
        {
            session.Title = Title(question.Text);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new AiStreamResult(async emit =>
        {
            var turn = await chat.RunAsync(connection.Value, History(session, before: null), null, emit, CancellationToken.None);
            if (turn.Error is not null)
            {
                session.Messages.Remove(question);
                db.Remove(question);
            }

            return await FinishAsync(session, turn);
        });
    }

    public async Task<IResult> ConfirmAsync(Guid sessionId, AiConfirmRequest request, CancellationToken cancellationToken)
    {
        var connection = await chat.ConnectAsync(cancellationToken);
        if (connection.Error is { } unavailable)
        {
            return unavailable.ToProblem();
        }

        if (await LoadAsync(sessionId, cancellationToken) is not { } session)
        {
            return NotFound.ToProblem();
        }

        var waiting = session.Messages.OrderBy(m => m.At).LastOrDefault();
        if (waiting is not { Pending: not null, Decision: null })
        {
            return Error.Conflict("ai.nothing_to_confirm", "Nothing waits for a confirmation.").ToProblem();
        }

        // decided before it runs: a second click or tab finds nothing to confirm
        waiting.Decision = request.Approve ? AiDecisions.Approved : AiDecisions.Declined;
        await db.SaveChangesAsync(cancellationToken);
        return new AiStreamResult(async emit =>
        {
            var turn = await chat.RunAsync(
                connection.Value, History(session, before: waiting), ct => chat.DecideAsync(waiting.Text, waiting.PendingCalls, request.Approve, emit, ct), emit, CancellationToken.None);
            if (turn.Error is { Code: "ai.busy" })
            {
                waiting.Decision = null;
            }

            return await FinishAsync(session, turn);
        });
    }

    private static readonly Error NotFound = Error.NotFound("ai.session_not_found", "The conversation does not exist.");

    private Task<AiChatSession?> LoadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        AiSessionHandlers.Mine(db, currentUser).Include(s => s.Messages).SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    private async Task<AiStreamEvent> FinishAsync(AiChatSession session, Result<AiTurn> turn)
    {
        if (turn.Error is { } failed)
        {
            session.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            return new AiStreamEvent(AiStreamEvent.Error, Text: failed.Message);
        }

        var answer = Add(session, "assistant", turn.Value.Text);
        answer.ToolCalls = turn.Value.Calls.Count > 0 ? JsonSerializer.Serialize(turn.Value.Calls, JsonSerializerOptions.Web) : null;
        answer.Pending = turn.Value.Pending is { } pending ? JsonSerializer.Serialize(pending, JsonSerializerOptions.Web) : null;
        await db.SaveChangesAsync(CancellationToken.None);
        return new AiStreamEvent(AiStreamEvent.Done, Message: answer.ToDto());
    }

    private AiChatMessage Add(AiChatSession session, string role, string text)
    {
        var now = clock.GetUtcNow();
        var message = new AiChatMessage { SessionId = session.Id, Role = role, Text = text, At = now };
        session.Messages.Add(message);
        db.Add(message);
        session.UpdatedAt = now;
        return message;
    }

    /// <summary>The newest turns that fit the limits, starting with a question; text only, the tool details stay in the audit.</summary>
    private static List<ChatMessage> History(AiChatSession session, AiChatMessage? before)
    {
        var history = new List<AiChatMessage>();
        var length = 0;
        foreach (var message in session.Messages.Where(m => m.Text.Length > 0 && (before is null || m.At < before.At)).OrderByDescending(m => m.At))
        {
            length += message.Text.Length;
            if (history.Count >= MaxHistory || (length > AiChat.MaxConversationLength && history.Count > 0))
            {
                break;
            }

            history.Insert(0, message);
        }

        return [.. history.SkipWhile(m => m.Role != "user").Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Text))];
    }

    private static string Title(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > MaxTitleLength ? line[..MaxTitleLength].TrimEnd() + "…" : line;
    }
}

/// <summary>Writes the events of a turn as JSON lines, flushed one by one; the last line is the saved message or the error.</summary>
internal sealed class AiStreamResult(Func<Func<AiStreamEvent, Task>, Task<AiStreamEvent>> run) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.ContentType = "application/x-ndjson";
        response.Headers.CacheControl = "no-cache";
        httpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        var connected = true;

        async Task WriteAsync(AiStreamEvent streamEvent)
        {
            if (!connected)
            {
                return;
            }

            try
            {
                await response.WriteAsync(JsonSerializer.Serialize(streamEvent, JsonSerializerOptions.Web) + "\n", CancellationToken.None);
                await response.Body.FlushAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // the browser left: the turn goes on and is saved for the other tabs
                connected = false;
            }
        }

        await WriteAsync(await run(WriteAsync));
    }
}
