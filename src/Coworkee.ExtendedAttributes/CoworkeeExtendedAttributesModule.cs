using Coworkee.Application;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.ExtendedAttributes;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.ExtendedAttributes;

/// <summary>Free key/value attributes on any registered entity, typed as text, decimal, date or JSON.</summary>
[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeExtendedAttributesModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(CoworkeeExtendedAttributesModule).Assembly);
        context.Services.AddSingleton<IModelContributor, ExtendedAttributeModelContributor>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var attributes = app.MapCoworkeeApi("/api/v1/attributes").WithTags("Extended attributes").RequireAuthorization();
        attributes.MapGet("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetExtendedAttributesQuery(entityType, entityId), ct).ToHttpResult());
        attributes.MapPut("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, SaveExtendedAttributesRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetExtendedAttributesCommand(entityType, entityId, body.Attributes), ct).ToHttpResult());
    }
}
