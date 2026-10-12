using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai.Sessions;

[AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Chat)]
public sealed record GetAiSessions(string? Search) : IQuery<Result<IReadOnlyList<AiSessionDto>>>;

[AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Chat)]
public sealed record GetAiSession(Guid Id) : IQuery<Result<AiSessionDetailDto>>;

[AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Chat)]
public sealed record CreateAiSession(string? Title) : ICommand<Result<AiSessionDto>>;

[AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Chat)]
public sealed record RenameAiSession(Guid Id, string Title) : ICommand<Result>;

[AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Chat)]
public sealed record DeleteAiSession(Guid Id) : ICommand<Result>;

internal sealed class RenameAiSessionValidator : AbstractValidator<RenameAiSession>
{
    public RenameAiSessionValidator() => RuleFor(c => c.Title).NotEmpty().MaximumLength(AiSessionLimits.Title);
}

internal sealed class CreateAiSessionValidator : AbstractValidator<CreateAiSession>
{
    public CreateAiSessionValidator() => RuleFor(c => c.Title).MaximumLength(AiSessionLimits.Title);
}

/// <summary>Sessions belong to their user alone: every query starts from <see cref="Mine"/>.</summary>
internal sealed class AiSessionHandlers(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IHandler<GetAiSessions, Result<IReadOnlyList<AiSessionDto>>>, IHandler<GetAiSession, Result<AiSessionDetailDto>>,
      IHandler<CreateAiSession, Result<AiSessionDto>>, IHandler<RenameAiSession, Result>, IHandler<DeleteAiSession, Result>
{
    public const string NewTitle = "New conversation";

    private static readonly Error NotFound = Error.NotFound("ai.session_not_found", "The conversation does not exist.");

    public async Task<Result<IReadOnlyList<AiSessionDto>>> HandleAsync(GetAiSessions query, CancellationToken cancellationToken)
    {
        var sessions = Mine(db, currentUser).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            sessions = sessions.Where(s => s.Title.ToLower().Contains(search) || s.Messages.Any(m => m.Text.ToLower().Contains(search)));
        }

        return await sessions.OrderByDescending(s => s.UpdatedAt).Take(AiSessionLimits.Sessions)
            .Select(s => new AiSessionDto(s.Id, s.Title, s.UpdatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<Result<AiSessionDetailDto>> HandleAsync(GetAiSession query, CancellationToken cancellationToken)
    {
        var session = await Mine(db, currentUser).AsNoTracking().Include(s => s.Messages).SingleOrDefaultAsync(s => s.Id == query.Id, cancellationToken);
        return session is null
            ? NotFound
            : new AiSessionDetailDto(session.Id, session.Title, session.UpdatedAt, [.. session.Messages.OrderBy(m => m.At).Select(m => m.ToDto())]);
    }

    public Task<Result<AiSessionDto>> HandleAsync(CreateAiSession command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = new AiChatSession
        {
            TenantId = currentUser.TenantId ?? Guid.Empty,
            UserId = currentUser.UserId!.Value,
            Title = string.IsNullOrWhiteSpace(command.Title) ? NewTitle : command.Title.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Add(session);
        return Task.FromResult<Result<AiSessionDto>>(session.ToDto());
    }

    public async Task<Result> HandleAsync(RenameAiSession command, CancellationToken cancellationToken)
    {
        var session = await Mine(db, currentUser).SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (session is null)
        {
            return NotFound;
        }

        session.Title = command.Title.Trim();
        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteAiSession command, CancellationToken cancellationToken)
    {
        var session = await Mine(db, currentUser).Include(s => s.Messages).SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (session is null)
        {
            return NotFound;
        }

        db.Remove(session);
        return Result.Success();
    }

    internal static IQueryable<AiChatSession> Mine(CoworkeeDbContext db, ICurrentUser currentUser)
    {
        var userId = currentUser.UserId;
        return db.Set<AiChatSession>().Where(s => s.UserId == userId);
    }
}
