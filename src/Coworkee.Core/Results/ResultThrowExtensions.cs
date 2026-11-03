namespace Coworkee.Core.Results;

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
