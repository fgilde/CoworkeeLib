using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

/// <summary>Which entity types take comments, tags, ratings and reactions; entity types are opt-in.</summary>
public sealed class SocialOptions
{
    internal Dictionary<(SocialFeature Feature, string EntityType), SocialTarget> Targets { get; } = [];

    internal List<Func<IServiceProvider, SocialAccess, CancellationToken, Task<bool>>> AccessChecks { get; } = [];

    internal List<Func<IServiceProvider, CommentValidation, CancellationToken, Task<Error?>>> CommentChecks { get; } = [];

    /// <summary>Comments on any id of <paramref name="entityType"/>; the id is not checked.</summary>
    public SocialOptions Comments(string entityType) => Add(SocialFeature.Comments, entityType, new(null, null, null));

    /// <summary>Comments on existing <typeparamref name="TEntity"/>s; the creator of an audited entity and everyone in the thread are notified, <paramref name="link"/> leads there.</summary>
    public SocialOptions Comments<TEntity>(string entityType, Func<Guid, string>? link = null)
        where TEntity : class => Add(SocialFeature.Comments, entityType, For<TEntity>(link));

    public SocialOptions Tags(string entityType) => Add(SocialFeature.Tags, entityType, new(null, null, null));

    public SocialOptions Tags<TEntity>(string entityType)
        where TEntity : class => Add(SocialFeature.Tags, entityType, For<TEntity>(null));

    public SocialOptions Ratings(string entityType) => Add(SocialFeature.Ratings, entityType, new(null, null, null));

    public SocialOptions Ratings<TEntity>(string entityType)
        where TEntity : class => Add(SocialFeature.Ratings, entityType, For<TEntity>(null));

    public SocialOptions Reactions(string entityType) => Add(SocialFeature.Reactions, entityType, new(null, null, null));

    public SocialOptions Reactions<TEntity>(string entityType)
        where TEntity : class => Add(SocialFeature.Reactions, entityType, For<TEntity>(null));

    /// <summary>Reactions on the comments of every entity type with comments; who may see or write the comments may see or add reactions there (with the reaction permissions).</summary>
    public SocialOptions CommentReactions() => Add(SocialFeature.Reactions, SocialReactions.Comment, new(null, null, null));

    /// <summary>An extra check on top of the permissions and the row visibility of the entity's OData set, e.g. for entity types without a set; every check must pass.</summary>
    public SocialOptions Authorize(Func<IServiceProvider, SocialAccess, CancellationToken, Task<bool>> check)
    {
        AccessChecks.Add(check);
        return this;
    }

    /// <summary>
    /// A check before a comment is added, edited, gets a status or is deleted, e.g. no comments on a booked quotation; the first error is the answer.
    /// Runs after the permissions and the access checks.
    /// </summary>
    public SocialOptions ValidateComments(Func<IServiceProvider, CommentValidation, CancellationToken, Task<Error?>> check)
    {
        CommentChecks.Add(check);
        return this;
    }

    private SocialOptions Add(SocialFeature feature, string entityType, SocialTarget target)
    {
        Targets[(feature, entityType)] = target;
        return this;
    }

    private static SocialTarget For<TEntity>(Func<Guid, string>? link)
        where TEntity : class
    {
        ODataEntityAccess.RequireGuidId<TEntity>("Social");
        return new(
            ODataEntityAccess.VisibleIdsAsync<TEntity>,
            typeof(IAuditable).IsAssignableFrom(typeof(TEntity))
                ? (db, id, cancellationToken) => db.Set<TEntity>().Where(e => EF.Property<Guid>(e, "Id") == id).Select(e => EF.Property<Guid?>(e, nameof(IAuditable.CreatedBy))).FirstOrDefaultAsync(cancellationToken)
                : null,
            link,
            typeof(TEntity));
    }
}
