namespace Coworkee.Application.Messaging;

public sealed class PutAttribute(string route) : EndpointAttribute("PUT", route);
