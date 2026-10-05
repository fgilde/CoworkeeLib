using Microsoft.AspNetCore.Mvc;

namespace Coworkee.ResponseFilters.Tests;

[ApiController]
[Route("mvc/people")]
public sealed class PeopleController : ControllerBase
{
    [HttpGet("one")]
    public ResponseFilterTests.Person One() => new("Ada", "secret-token", "4111111111111111");

    [HttpGet]
    public IQueryable<ResponseFilterTests.Person> All() => new[] { One() }.AsQueryable();
}
