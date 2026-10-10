namespace Coworkee.Core.Results;

/// <summary>Carries an <see cref="Results.Error"/> through code that throws instead of returning results, such as application services; HTTP answers it as problem details.</summary>
public sealed class ErrorException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}

public static class ResultThrowExtensions
{
    public static async Task<T> OrThrow<T>(this Task<Result<T>> task)
    {
        var result = await task;
        return result.IsSuccess ? result.Value : throw new ErrorException(result.Error);
    }

    public static async Task OrThrow(this Task<Result> task)
    {
        var result = await task;
        if (!result.IsSuccess)
        {
            throw new ErrorException(result.Error);
        }
    }
}
