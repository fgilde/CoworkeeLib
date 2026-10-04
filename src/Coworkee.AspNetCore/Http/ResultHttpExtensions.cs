using Coworkee.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Coworkee.AspNetCore.Http;

/// <summary>
/// Maps results to HTTP. The typed result unions tell OpenAPI the response schema of every endpoint, so generated
/// clients know what comes back.
/// </summary>
public static class ResultHttpExtensions
{
    public static async Task<Results<Ok<T>, ProblemHttpResult, ValidationProblem>> ToHttpResult<T>(this Task<Result<T>> task)
    {
        var result = await task;
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return result.Error!.Kind == ErrorKind.Validation ? Validation(result.Error) : Problem(result.Error);
    }

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> ToHttpResult(this Task<Result> task)
    {
        var result = await task;
        if (result.IsSuccess)
        {
            return TypedResults.NoContent();
        }

        return result.Error!.Kind == ErrorKind.Validation ? Validation(result.Error) : Problem(result.Error);
    }

    public static IResult ToProblem(this Error error) => error.Kind == ErrorKind.Validation ? Validation(error) : Problem(error);

    private static ValidationProblem Validation(Error error) =>
        TypedResults.ValidationProblem(error.Details?.ToDictionary() ?? [], title: error.Message, extensions: Extensions(error));

    private static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(title: error.Message, statusCode: StatusCodeOf(error.Kind), extensions: Extensions(error));

    private static Dictionary<string, object?> Extensions(Error error) => new(StringComparer.Ordinal) { ["code"] = error.Code };

    private static int StatusCodeOf(ErrorKind kind) => kind switch
    {
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
