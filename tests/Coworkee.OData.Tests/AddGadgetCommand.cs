using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using FluentValidation;

namespace Coworkee.OData.Tests;

public sealed record AddGadgetCommand(string Name, string Category, decimal Price) : ICommand<Result<Guid>>;

internal sealed class AddGadgetValidator : AbstractValidator<AddGadgetCommand>
{
    public AddGadgetValidator() => RuleFor(c => c.Name).NotEmpty();
}

internal sealed class AddGadgetHandler(GadgetDbContext db, ICurrentUser user) : IHandler<AddGadgetCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(AddGadgetCommand request, CancellationToken cancellationToken)
    {
        var gadget = new Gadget { Name = request.Name, Category = request.Category, Price = request.Price, TenantId = user.TenantId!.Value };
        db.Add(gadget);
        await db.SaveChangesAsync(cancellationToken);
        return gadget.Id;
    }
}
