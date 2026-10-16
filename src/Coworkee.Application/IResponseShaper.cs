namespace Coworkee.Application;

/// <summary>Shapes a value as the API answers it, for answers written outside an endpoint result (streams); the response filters module plugs in.</summary>
public interface IResponseShaper
{
    Task<object?> ShapeAsync(object value, CancellationToken cancellationToken);
}
