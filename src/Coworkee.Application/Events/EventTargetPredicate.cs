using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Events;

/// <summary>A target as one predicate over <see cref="EventUser"/>, for the user directory to run in the database.</summary>
internal static class EventTargetPredicate
{
    private static readonly ParameterExpression User = Expression.Parameter(typeof(EventUser), "u");

    /// <summary>Custom audiences are asked first; their ids join the predicate as a list.</summary>
    public static async Task<Expression<Func<EventUser, bool>>> BuildAsync(EventTarget target, Guid? current, IServiceProvider services, CancellationToken cancellationToken) =>
        Expression.Lambda<Func<EventUser, bool>>(await BodyAsync(target, current, services, cancellationToken), User);

    private static async Task<Expression> BodyAsync(EventTarget target, Guid? current, IServiceProvider services, CancellationToken cancellationToken) => target switch
    {
        AllTarget => Expression.Constant(true),
        CurrentTarget => current is { } id ? IdIs(id) : Expression.Constant(false),
        UsersTarget users => IdIn(users.Ids),
        CustomTarget custom => IdIn(await ((IAudienceResolver)(services.GetService(custom.Resolver) ?? ActivatorUtilities.CreateInstance(services, custom.Resolver)))
            .ResolveAsync(custom.Args, cancellationToken)),
        WhereTarget where => new Rebind(where.Predicate.Parameters[0], User).Visit(where.Predicate.Body),
        UnionTarget union => Expression.OrElse(await BodyAsync(union.Left, current, services, cancellationToken), await BodyAsync(union.Right, current, services, cancellationToken)),
        ExceptTarget except => Expression.AndAlso(
            await BodyAsync(except.Target, current, services, cancellationToken), Expression.Not(await BodyAsync(except.Excluded, current, services, cancellationToken))),
        _ => throw new NotSupportedException($"{target} reaches every organisation and is for realtime and toasts only."),
    };

    private static BinaryExpression IdIs(Guid id) => Expression.Equal(Expression.Property(User, nameof(EventUser.Id)), Expression.Constant(id));

    // a list parameter, not a constant: the database plans one statement for every list
    private static MethodCallExpression IdIn(IReadOnlyCollection<Guid> ids) =>
        Expression.Call(Expression.Field(Expression.Constant(new Holder([.. ids])), nameof(Holder.Value)), typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains))!,
            Expression.Property(User, nameof(EventUser.Id)));

    private sealed class Holder(List<Guid> value)
    {
        public readonly List<Guid> Value = value;
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
