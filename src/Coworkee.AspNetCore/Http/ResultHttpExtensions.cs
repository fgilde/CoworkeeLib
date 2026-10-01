using Coworkee.Core.Results;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

public static class ResultHttpExtensions
{
    public static async Task<IResult> ToHttpResult<T>(this Task<Result<T>> task)
    {
        var result = await task;
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    public static async Task<IResult> ToHttpResult(this Task<Result> task)
    {
        var result = await task;
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };
        return error.Kind == ErrorKind.Validation
            ? TypedResults.ValidationProblem(error.Details?.ToDictionary() ?? [], title: error.Message, extensions: extensions)
            : TypedResults.Problem(title: error.Message, statusCode: StatusCodeOf(error.Kind), extensions: extensions);
    }

    private static int StatusCodeOf(ErrorKind kind) => kind switch
    {
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
