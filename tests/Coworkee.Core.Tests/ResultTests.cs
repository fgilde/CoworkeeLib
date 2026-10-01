using Coworkee.Core.Results;

namespace Coworkee.Core.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_has_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_throws_on_value_access()
    {
        Result<int> result = Error.NotFound("item.not_found", "Item not found");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Factory_creates_failure_for_result_types()
    {
        var error = Error.Conflict("x", "y");

        ResultFactory.TryCreateFailure<Result>(error, out var plain).ShouldBeTrue();
        plain.Error.ShouldBe(error);
        ResultFactory.TryCreateFailure<Result<string>>(error, out var typed).ShouldBeTrue();
        typed.Error.ShouldBe(error);
    }

    [Fact]
    public void Factory_refuses_non_result_types() =>
        ResultFactory.TryCreateFailure<string>(Error.Unexpected("x", "y"), out _).ShouldBeFalse();
}
