using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.ClientEntities;

[RequiresPermission(SettingsPermissions.Manage)]
[Description("Lists every entity set clients could keep a local copy of, with its effective client entity settings and what decided each value.")]
public sealed record GetClientEntityAdmin : IQuery<Result<IReadOnlyList<ClientEntityAdminSetDto>>>;

internal sealed class GetClientEntityAdminHandler(ODataEntityRegistry registry, IOptionsMonitor<ClientEntityOptions> options, IOptions<CoworkeeODataOptions> odata)
    : IHandler<GetClientEntityAdmin, Result<IReadOnlyList<ClientEntityAdminSetDto>>>
{
    public Task<Result<IReadOnlyList<ClientEntityAdminSetDto>>> HandleAsync(GetClientEntityAdmin query, CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        IReadOnlyList<ClientEntityAdminSetDto> sets =
        [
            .. registry.Entities.Where(e => e.HasGuidId).OrderBy(e => e.EntitySet, StringComparer.OrdinalIgnoreCase).Select(entity =>
            {
                var (settings, sources) = current.Describe(entity.EntityType, entity.EntitySet);
                var copy = !settings.Enabled ? ClientEntityCopy.Off : settings.Copy;
                // the batch the clients get: the manifest caps it by the OData page size
                return new ClientEntityAdminSetDto(entity.EntitySet, entity.EntityType.Name, copy, settings.MaxRows, Math.Min(settings.BatchSize, odata.Value.MaxTop),
                    Math.Max(1, settings.ParallelPages), settings.RefreshInterval, settings.DefaultMode, sources,
                    current.Sets.GetValueOrDefault(entity.EntitySet) is { IsEmpty: false } set ? set : null);
            }),
        ];
        return Task.FromResult(Result<IReadOnlyList<ClientEntityAdminSetDto>>.Success(sets));
    }
}
