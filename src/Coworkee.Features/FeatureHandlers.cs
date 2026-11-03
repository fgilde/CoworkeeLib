using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;

namespace Coworkee.Features;

internal sealed class FeatureHandlers(IFeatureChecker features, IFeatureDefinitionManager definitions)
    : IHandler<GetFeatures, Result<IReadOnlyDictionary<string, string?>>>, IHandler<GetFeatureDefinitions, Result<IReadOnlyList<FeatureGroupDto>>>
{
    public async Task<Result<IReadOnlyDictionary<string, string?>>> HandleAsync(GetFeatures query, CancellationToken cancellationToken) =>
        Result<IReadOnlyDictionary<string, string?>>.Success(await features.GetAllAsync(cancellationToken));

    public Task<Result<IReadOnlyList<FeatureGroupDto>>> HandleAsync(GetFeatureDefinitions query, CancellationToken cancellationToken)
    {
        IReadOnlyList<FeatureGroupDto> groups = definitions.Groups
            .Select(g => new FeatureGroupDto(g.Name, g.DisplayName, definitions.All
                .Where(d => d.Group == g.Name)
                .Select(d => new FeatureDefinitionDto(d.Name, d.DisplayName, d.Description, d.Type, d.DefaultValue))
                .ToList()))
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<FeatureGroupDto>>.Success(groups));
    }
}
