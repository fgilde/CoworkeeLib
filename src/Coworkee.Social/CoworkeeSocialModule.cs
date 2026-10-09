using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Social;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Social;

/// <summary>Comments, tags and ratings on any entity type an app opts in with AddCoworkeeSocial.</summary>
[DependsOn(typeof(CoworkeeRealtimeModule))]
public sealed class CoworkeeSocialModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddCoworkeeSocial(_ => { });
        services.AddMessagingFromAssembly(typeof(CoworkeeSocialModule).Assembly);
        services.AddSingleton<IModelContributor, SocialModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, SocialPermissionDefinitions>();
        services.AddScoped<SocialGuard>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, SocialPersonalData>();
        services.AddScoped<IRealtimeTopicAuthorizer, CommentTopicAuthorizer>();
        services.TryAddSingleton(TimeProvider.System);
    }

    public void ConfigureApplication(WebApplication app)
    {
        var comments = app.MapCoworkeeApi("/api/v1/comments").WithTags("Comments").RequireAuthorization();
        comments.MapGet("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetCommentsQuery(entityType, entityId), ct).ToHttpResult());
        comments.MapPost("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, CommentRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new AddCommentCommand(entityType, entityId, body.Text, body.ParentId), ct).ToHttpResult());
        comments.MapPut("/{id:guid}", (Guid id, CommentRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new EditCommentCommand(id, body.Text), ct).ToHttpResult());
        comments.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteCommentCommand(id), ct).ToHttpResult());

        var tags = app.MapCoworkeeApi("/api/v1/tags").WithTags("Tags").RequireAuthorization();
        tags.MapGet("/{entityType}", (string entityType, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetTagsQuery(entityType), ct).ToHttpResult());
        tags.MapDelete("/{entityType}", (string entityType, string tag, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new DeleteTagCommand(entityType, tag), ct).ToHttpResult());
        tags.MapGet("/{entityType}/entities", (string entityType, string tag, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetTaggedEntitiesQuery(entityType, tag), ct).ToHttpResult());
        tags.MapGet("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetEntityTagsQuery(entityType, entityId), ct).ToHttpResult());
        tags.MapPut("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, SaveTagsRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetEntityTagsCommand(entityType, entityId, body.Tags), ct).ToHttpResult());

        var ratings = app.MapCoworkeeApi("/api/v1/ratings").WithTags("Ratings").RequireAuthorization();
        ratings.MapGet("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetRatingQuery(entityType, entityId), ct).ToHttpResult());
        ratings.MapPut("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, RateRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RateCommand(entityType, entityId, body.Stars), ct).ToHttpResult());
        ratings.MapDelete("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, Guid? userId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new ClearRatingCommand(entityType, entityId, userId), ct).ToHttpResult());
    }
}

internal sealed class SocialPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(SocialPermissions.GroupName, "Comments, tags and ratings")
            .Add(SocialPermissions.Comments.View, "View comments")
            .Add(SocialPermissions.Comments.Create, "Write comments", SocialPermissions.Comments.View)
            .Add(SocialPermissions.Comments.Moderate, "Moderate comments", SocialPermissions.Comments.Create)
            .Add(SocialPermissions.Tags.View, "View tags")
            .Add(SocialPermissions.Tags.Create, "Tag entities", SocialPermissions.Tags.View)
            .Add(SocialPermissions.Tags.Moderate, "Manage tag sets", SocialPermissions.Tags.Create)
            .Add(SocialPermissions.Ratings.View, "View ratings")
            .Add(SocialPermissions.Ratings.Create, "Rate entities", SocialPermissions.Ratings.View)
            .Add(SocialPermissions.Ratings.Moderate, "Moderate ratings", SocialPermissions.Ratings.Create);
}
