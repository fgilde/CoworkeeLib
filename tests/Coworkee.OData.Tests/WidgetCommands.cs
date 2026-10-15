using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Tests;

public sealed class WidgetRow
{
    public string Name { get; set; } = string.Empty;

    public WidgetKind Kind { get; set; }

    public bool Active { get; set; }

    public decimal Price { get; set; }

    public DateTimeOffset MadeAt { get; set; }

    public string Color { get; set; } = string.Empty;
}

[RequiresPermission(ODataApp.ViewGadgets)]
public sealed record SaveWidgetCommand(Guid? Id, WidgetRow Row) : ICommand<Result<Guid>>;

public sealed record DeleteWidgetCommand(Guid Id) : ICommand<Result>;

internal sealed class SaveWidgetValidator : AbstractValidator<SaveWidgetCommand>
{
    public SaveWidgetValidator()
    {
        RuleFor(c => c.Row.Name).NotEmpty().OverridePropertyName("Name");
        RuleFor(c => c.Row.Price).GreaterThanOrEqualTo(0).OverridePropertyName("Price");
    }
}

internal sealed class WidgetHandlers(GadgetDbContext db, ICurrentUser user) : IHandler<SaveWidgetCommand, Result<Guid>>, IHandler<DeleteWidgetCommand, Result>
{
    public async Task<Result<Guid>> HandleAsync(SaveWidgetCommand request, CancellationToken cancellationToken)
    {
        var widget = request.Id is { } id ? await db.Set<Widget>().SingleOrDefaultAsync(w => w.Id == id, cancellationToken) : null;
        if (request.Id is not null && widget is null)
        {
            return Error.NotFound("widget.not_found", "No such widget.");
        }

        if (widget is null)
        {
            widget = new Widget { Name = request.Row.Name, Color = request.Row.Color, TenantId = user.TenantId!.Value };
            db.Add(widget);
        }

        (widget.Name, widget.Kind, widget.Active, widget.Price, widget.MadeAt, widget.Color) =
            (request.Row.Name, request.Row.Kind, request.Row.Active, request.Row.Price, request.Row.MadeAt, request.Row.Color);
        await db.SaveChangesAsync(cancellationToken);
        return widget.Id;
    }

    public async Task<Result> HandleAsync(DeleteWidgetCommand request, CancellationToken cancellationToken)
    {
        await db.Set<Widget>().Where(w => w.Id == request.Id).ExecuteDeleteAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record AddPartCommand(string Name, int Quantity, Guid WidgetId) : ICommand<Result<Guid>>;

internal sealed class AddPartHandler(GadgetDbContext db, ICurrentUser user) : IHandler<AddPartCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(AddPartCommand request, CancellationToken cancellationToken)
    {
        var part = new Part { Name = request.Name, Quantity = request.Quantity, WidgetId = request.WidgetId, TenantId = user.TenantId!.Value };
        db.Add(part);
        await db.SaveChangesAsync(cancellationToken);
        return part.Id;
    }
}
