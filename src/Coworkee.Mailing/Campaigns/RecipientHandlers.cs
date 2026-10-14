using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record PreviewRecipients(RecipientSourceDto Source) : IQuery<Result<RecipientPreviewDto>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record GetRecipientLists : IQuery<Result<IReadOnlyList<RecipientListDto>>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record CreateRecipientList(SaveRecipientListRequest Request) : ICommand<Result<RecipientListDto>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record UpdateRecipientList(Guid Id, SaveRecipientListRequest Request) : ICommand<Result>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record DeleteRecipientList(Guid Id) : ICommand<Result>;

internal static class RecipientSourceJson
{
    public static string Write(RecipientSourceDto source) => JsonSerializer.Serialize(source, JsonSerializerOptions.Web);

    public static RecipientSourceDto Read(string json) => JsonSerializer.Deserialize<RecipientSourceDto>(json, JsonSerializerOptions.Web)!;
}

internal sealed class RecipientHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IMailRecipientDirectory directory, TimeProvider clock)
    : IHandler<PreviewRecipients, Result<RecipientPreviewDto>>,
      IHandler<GetRecipientLists, Result<IReadOnlyList<RecipientListDto>>>,
      IHandler<CreateRecipientList, Result<RecipientListDto>>,
      IHandler<UpdateRecipientList, Result>,
      IHandler<DeleteRecipientList, Result>
{
    private const int PreviewRows = 20;

    private static readonly Error NotFound = Error.NotFound("mail.recipient_list_not_found", "The recipient list does not exist.");

    public Task<Result<RecipientPreviewDto>> HandleAsync(PreviewRecipients query, CancellationToken cancellationToken) =>
        directory.PreviewAsync(query.Source, PreviewRows, cancellationToken);

    public async Task<Result<IReadOnlyList<RecipientListDto>>> HandleAsync(GetRecipientLists query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var lists = await db.Set<RecipientList>().AsNoTracking().Where(l => l.TenantId == tenantId).OrderBy(l => l.Name).ToListAsync(cancellationToken);
        return lists.Select(l => new RecipientListDto(l.Id, l.Name, RecipientSourceJson.Read(l.Source))).ToList();
    }

    public async Task<Result<RecipientListDto>> HandleAsync(CreateRecipientList command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var name = request.Name?.Trim() ?? string.Empty;
        if (await CheckAsync(null, name, request.Source, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var list = new RecipientList { TenantId = currentUser.TenantId, Name = name, Source = RecipientSourceJson.Write(request.Source), CreatedAt = clock.GetUtcNow() };
        db.Add(list);
        return new RecipientListDto(list.Id, name, request.Source);
    }

    public async Task<Result> HandleAsync(UpdateRecipientList command, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        if (await db.Set<RecipientList>().FirstOrDefaultAsync(l => l.Id == command.Id && l.TenantId == tenantId, cancellationToken) is not { } list)
        {
            return NotFound;
        }

        var name = command.Request.Name?.Trim() ?? string.Empty;
        if (await CheckAsync(list.Id, name, command.Request.Source, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        list.Name = name;
        list.Source = RecipientSourceJson.Write(command.Request.Source);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteRecipientList command, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        if (await db.Set<RecipientList>().FirstOrDefaultAsync(l => l.Id == command.Id && l.TenantId == tenantId, cancellationToken) is not { } list)
        {
            return NotFound;
        }

        db.Remove(list);
        return Result.Success();
    }

    private async Task<Error?> CheckAsync(Guid? id, string name, RecipientSourceDto source, CancellationToken cancellationToken)
    {
        if (name.Length is 0 or > 256)
        {
            return Error.Validation("Name", "A name of up to 256 characters is required.");
        }

        var tenantId = currentUser.TenantId;
        if (await db.Set<RecipientList>().AnyAsync(l => l.TenantId == tenantId && l.Name == name && l.Id != id, cancellationToken))
        {
            return Error.Validation("Name", $"A recipient list named '{name}' already exists.");
        }

        return (await directory.PreviewAsync(source, 0, cancellationToken)).Error;
    }
}
