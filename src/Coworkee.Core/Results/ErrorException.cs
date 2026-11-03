namespace Coworkee.Core.Results;

/// <summary>Carries an <see cref="Results.Error"/> through code that throws instead of returning results, such as application services; HTTP answers it as problem details.</summary>
public sealed class ErrorException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}
