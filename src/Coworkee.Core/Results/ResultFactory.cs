using System.Diagnostics.CodeAnalysis;

namespace Coworkee.Core.Results;

public static class ResultFactory
{
    public static bool TryCreateFailure<TResult>(Error error, [MaybeNullWhen(false)] out TResult result)
    {
        if (FailureFactory<TResult>.Create is { } create)
        {
            result = create(error);
            return true;
        }

        result = default;
        return false;
    }

    private static class FailureFactory<TResult>
    {
        public static readonly Func<Error, TResult>? Create = Build();

        private static Func<Error, TResult>? Build()
        {
            var type = typeof(TResult);
            if (type == typeof(Result))
            {
                return error => (TResult)(object)Result.Failure(error);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
            {
                return type.GetMethod(nameof(Result.Failure), [typeof(Error)])!.CreateDelegate<Func<Error, TResult>>();
            }

            return null;
        }
    }
}
