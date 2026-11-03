namespace Coworkee.Application.Messaging;

public sealed class GetAttribute(string route) : EndpointAttribute("GET", route);
