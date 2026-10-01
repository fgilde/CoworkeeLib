namespace Coworkee.Core;

public sealed class ForbiddenException(string message) : Exception(message);
