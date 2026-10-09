using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Social;

public enum SocialFeature
{
    Comments,
    Tags,
    Ratings,
}

/// <summary>What is about to happen: <see cref="EntityId"/> is null for questions about the whole entity type, such as the tag cloud.</summary>
public sealed record SocialAccess(SocialFeature Feature, string EntityType, Guid? EntityId, bool Write);

internal sealed record SocialTarget(
    Func<CoworkeeDbContext, Guid, CancellationToken, Task<bool>>? ExistsAsync,
    Func<CoworkeeDbContext, Guid, CancellationToken, Task<Guid?>>? OwnerAsync,
    Func<Guid, string>? Link);

/// <summary>Which entity types take comments, tags and ratings; entity types are opt-in.</summary>
public sealed class SocialOptions
{
    internal Dictionary<(SocialFeature Feature, string EntityType), SocialTarget> Targets { get; } = [];

    internal List<Func<IServiceProvider, SocialAccess, CancellationToken, Task<bool>>> AccessChecks { get; } = [];

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

    /// <summary>An extra check on top of the permissions, e.g. "may the user see this product at all"; every check must pass.</summary>
    public SocialOptions Authorize(Func<IServiceProvider, SocialAccess, CancellationToken, Task<bool>> check)
    {
        AccessChecks.Add(check);
        return this;
    }

    private SocialOptions Add(SocialFeature feature, string entityType, SocialTarget target)
    {
        Targets[(feature, entityType)] = target;
        return this;
    }

    private static SocialTarget For<TEntity>(Func<Guid, string>? link)
        where TEntity : class => new(
        (db, id, cancellationToken) => db.Set<TEntity>().AnyAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken),
        typeof(IAuditable).IsAssignableFrom(typeof(TEntity))
            ? (db, id, cancellationToken) => db.Set<TEntity>().Where(e => EF.Property<Guid>(e, "Id") == id).Select(e => EF.Property<Guid?>(e, nameof(IAuditable.CreatedBy))).FirstOrDefaultAsync(cancellationToken)
            : null,
        link);
}

public static class SocialServiceCollectionExtensions
{
    /// <summary>Opts entity types in, e.g. <c>services.AddCoworkeeSocial(s => s.Comments&lt;Product&gt;("Products").Tags&lt;Product&gt;("Products"))</c>; may be called by several modules.</summary>
    public static IServiceCollection AddCoworkeeSocial(this IServiceCollection services, Action<SocialOptions> configure)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(SocialOptions))?.ImplementationInstance is not SocialOptions options)
        {
            options = new SocialOptions();
            services.AddSingleton(options);
        }

        configure(options);
        return services;
    }
}
