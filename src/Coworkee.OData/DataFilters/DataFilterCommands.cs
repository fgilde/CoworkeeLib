using Coworkee.Application.Messaging;
using Coworkee.Contracts.DataFilters;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.OData.DataFilters;

// the permission depends on the layer: checked in the handler
[AiTool(Exclude = true)]
public sealed record SaveDataFilterCommand(SaveDataFilterRequest Request) : ICommand<Result<DataFilterDto>>;

[AiTool(Exclude = true)]
public sealed record DeleteDataFilterCommand(Guid Id) : ICommand<Result>;

[AiTool(Exclude = true)]
public sealed record GetDataFiltersQuery(string? EntitySet = null, DataFilterLayer? Layer = null, Guid? LayerKey = null) : IQuery<Result<IReadOnlyList<DataFilterDto>>>;

[AiTool(Exclude = true)]
public sealed record SaveDataFilterLinkCommand(SaveDataFilterLinkRequest Request) : ICommand<Result<DataFilterLinkDto>>;

[AiTool(Exclude = true)]
public sealed record DeleteDataFilterLinkCommand(Guid Id) : ICommand<Result>;

[AiTool(Exclude = true)]
public sealed record GetDataFilterLinksQuery : IQuery<Result<IReadOnlyList<DataFilterLinkDto>>>;

[AiTool(Exclude = true)]
public sealed record GetDataFilterFieldsQuery(string EntitySet) : IQuery<Result<IReadOnlyList<DataFilterFieldDto>>>;

[AiTool(Exclude = true)]
public sealed record GetDataFilterSetsQuery : IQuery<Result<IReadOnlyList<string>>>;

// the permission depends on the layer: checked in the handler
[AiTool(Exclude = true)]
public sealed record PreviewDataFilterQuery(PreviewDataFilterRequest Request) : IQuery<Result<DataFilterPreviewDto>>;

[AiTool(Exclude = true)]
public sealed record GetDataFilterOverviewQuery : IQuery<Result<DataFilterOverviewDto>>;

internal sealed class SaveDataFilterValidator : AbstractValidator<SaveDataFilterCommand>
{
    public SaveDataFilterValidator()
    {
        RuleFor(c => c.Request.EntitySet).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.Layer).IsInEnum();
        RuleFor(c => c.Request.Query).NotEmpty();
        RuleFor(c => c.Request.LayerKey).NotNull().WithMessage("Group and role filters name the group or role.")
            .When(c => c.Request.Layer is DataFilterLayer.Group or DataFilterLayer.Role);
        RuleFor(c => c.Request.LayerKey).Null().WithMessage("System and tenant filters have no group or role.")
            .When(c => c.Request.Layer is DataFilterLayer.System or DataFilterLayer.Tenant);
    }
}

internal sealed class PreviewDataFilterValidator : AbstractValidator<PreviewDataFilterQuery>
{
    public PreviewDataFilterValidator()
    {
        RuleFor(c => c.Request.EntitySet).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.Layer).IsInEnum();
        RuleFor(c => c.Request.Query).NotEmpty();
    }
}

internal sealed class SaveDataFilterLinkValidator : AbstractValidator<SaveDataFilterLinkCommand>
{
    public SaveDataFilterLinkValidator()
    {
        RuleFor(c => c.Request.EntitySet).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.Property).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.TargetSet).NotEmpty().MaximumLength(200);
    }
}
