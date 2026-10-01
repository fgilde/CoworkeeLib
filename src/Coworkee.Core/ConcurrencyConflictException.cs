namespace Coworkee.Core;

public sealed class ConcurrencyConflictException(string message, Exception innerException) : Exception(message, innerException);
