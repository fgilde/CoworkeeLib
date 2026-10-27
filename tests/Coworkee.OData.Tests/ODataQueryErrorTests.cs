using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>Clients read OData's own messages; other errors of a query (EF translating it) can quote row filters and stay in the log.</summary>
public sealed class ODataQueryErrorTests
{
    [Fact]
    public void Only_odata_messages_reach_the_client()
    {
        Detail(new Microsoft.OData.ODataException("Could not find a property named 'Nmae'.")).ShouldContain("Nmae");

        var detail = Detail(new InvalidOperationException("The LINQ expression 'Where(b => b.Name != \"Secret\")' could not be translated."));
        (detail ?? string.Empty).ShouldNotContain("Secret");
        (detail ?? string.Empty).ShouldNotContain("LINQ");
    }

    private static string? Detail(Exception exception)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        var errors = EnableQueryAttribute.CreateErrorResponse($"The query specified in the URI is not valid. {exception.Message}", exception);
        var context = new ResultExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], new BadRequestObjectResult(errors), new object());

        new ODataQueryErrorAttribute().OnResultExecuting(context);

        var result = context.Result.ShouldBeOfType<JsonResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        return result.Value.ShouldBeOfType<ProblemDetails>().Detail;
    }
}
