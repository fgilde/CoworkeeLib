using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

public sealed record ODataImportRegistration(string EntitySet, Type CommandType, Func<IDispatcher, object, CancellationToken, Task<Result>> Send)
{
    internal static ODataImportRegistration For<TCommand>(string entitySet)
    {
        var resultType = typeof(TCommand).GetInterfaces()
            .SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>) && i.GenericTypeArguments[0].IsAssignableTo(typeof(Result)))
            ?.GenericTypeArguments[0]
            ?? throw new InvalidOperationException($"{typeof(TCommand).Name} has to be a request that returns a Result to import rows.");
        var send = typeof(ODataImportRegistration).GetMethod(nameof(SendAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(resultType)
            .CreateDelegate<Func<IDispatcher, object, CancellationToken, Task<Result>>>();
        return new ODataImportRegistration(entitySet, typeof(TCommand), send);
    }

    private static async Task<Result> SendAsync<TResult>(IDispatcher dispatcher, object command, CancellationToken cancellationToken)
        where TResult : Result =>
        await dispatcher.SendAsync((IRequest<TResult>)command, cancellationToken);
}
