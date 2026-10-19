using System.Reflection;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.Transfer;

public sealed record ExportJobArgs(Guid JobId);

/// <summary>Writes an export too large for the request; the owner gets the file through the job (the client shows a download link).</summary>
public sealed class DataExports(IServiceProvider services, CoworkeeDbContext db, ODataEntityRegistry registry, ICurrentUser user, TimeProvider clock, IOptions<CoworkeeODataOptions> options)
{
    private static readonly MethodInfo WriteMethod = typeof(DataExport).GetMethod(nameof(DataExport.WriteAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The file, or the job that writes it when the rows are more than <see cref="CoworkeeODataOptions.BackgroundExportRows"/>.</summary>
    public async Task<IResult> ExportAsync(string entitySet, QueryString query, CancellationToken cancellationToken)
    {
        if (registry.Find(entitySet) is not { } entity)
        {
            return Results.NotFound();
        }

        if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
        {
            return Results.Forbid();
        }

        ExportFile? file;
        try
        {
            file = await WriteAsync(entity, query, options.Value.BackgroundExportRows, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        if (file is not null)
        {
            return Results.File(file.Content, file.ContentType, file.Name);
        }

        var job = new DataTransferJob
        {
            TenantId = user.TenantId ?? Guid.Empty,
            UserId = user.UserId!.Value,
            Kind = DataTransferKind.Export,
            EntitySet = entity.EntitySet,
            Status = DataTransferStatus.Queued,
            Settings = query.Value,
            FileName = $"{entity.EntitySet}.{DataExport.Format(FormatOf(query)).ToString().ToLowerInvariant()}",
            CreatedAt = clock.GetUtcNow(),
        };
        db.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        services.GetRequiredService<IBackgroundJobs>().Enqueue<ExportJob, ExportJobArgs>(new ExportJobArgs(job.Id));
        var dto = await services.GetRequiredService<DataTransferJobs>().GetAsync(job.Id, cancellationToken);
        return Results.Accepted($"/api/v1/data/jobs/{job.Id}", dto.Value);
    }

    internal Task<ExportFile?> WriteAsync(ODataEntityRegistration entity, QueryString query, int? maxRows, CancellationToken cancellationToken) =>
        (Task<ExportFile?>)WriteMethod.MakeGenericMethod(entity.EntityType).Invoke(null, [services, entity.EntitySet, query, maxRows, cancellationToken])!;

    private static string? FormatOf(QueryString query) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query.Value).TryGetValue("format", out var format) ? format.ToString() : null;
}
