using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

/// <summary>
/// How rows of an entity set become commands (requests that answer with a Result). <see cref="Update"/> (id, row) makes update
/// and upsert possible, <see cref="Delete"/> the undo of the rows a batch created.
/// </summary>
public sealed record ODataImportRegistration(
    string EntitySet,
    Type RowType,
    Func<object, object> Create,
    Func<Guid, object, object>? Update = null,
    Func<Guid, object>? Delete = null)
{
    private static readonly MethodInfo SendMethod = typeof(ODataImportRegistration).GetMethod(nameof(SendCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Task<Result> SendAsync(IDispatcher dispatcher, object command, CancellationToken cancellationToken) =>
        (Task<Result>)SendMethod.MakeGenericMethod(ResultType(command.GetType())).Invoke(null, [dispatcher, command, cancellationToken])!;

    /// <summary>The id a create command answered with: a Guid result or the Id of the returned row.</summary>
    public static Guid? CreatedId(Result result) =>
        result.GetType().GetProperty("Value")?.GetValue(result) switch
        {
            Guid id => id,
            { } value => value.GetType().GetProperty("Id")?.GetValue(value) as Guid?,
            _ => null,
        };

    /// <summary>A failed command as one line: the validation messages per field, else the error message.</summary>
    public static string Describe(Error error) =>
        error.Details is { Count: > 0 } details
            ? string.Join("; ", details.SelectMany(field => field.Value.Select(message => $"{field.Key}: {message}")))
            : error.Message;

    internal static ODataImportRegistration For<TCommand>(string entitySet)
    {
        ResultType(typeof(TCommand));
        return new ODataImportRegistration(entitySet, typeof(TCommand), row => row);
    }

    internal static ODataImportRegistration For<TRow, TResult>(string entitySet, Func<TRow, IRequest<TResult>> toCommand)
        where TResult : Result =>
        new(entitySet, typeof(TRow), row => toCommand((TRow)row));

    internal static ODataImportRegistration For<TRow, TResult>(string entitySet, Func<Guid?, TRow, IRequest<TResult>> toCommand, Func<Guid, IRequest<Result>>? delete)
        where TResult : Result =>
        new(entitySet, typeof(TRow), row => toCommand(null, (TRow)row), (id, row) => toCommand(id, (TRow)row), delete is null ? null : id => delete(id));

    private static Type ResultType(Type command) =>
        command.GetInterfaces()
            .SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>) && i.GenericTypeArguments[0].IsAssignableTo(typeof(Result)))
            ?.GenericTypeArguments[0]
        ?? throw new InvalidOperationException($"{command.Name} has to be a request that returns a Result to import rows.");

    private static async Task<Result> SendCoreAsync<TResult>(IDispatcher dispatcher, object command, CancellationToken cancellationToken)
        where TResult : Result =>
        await dispatcher.SendAsync((IRequest<TResult>)command, cancellationToken);
}
