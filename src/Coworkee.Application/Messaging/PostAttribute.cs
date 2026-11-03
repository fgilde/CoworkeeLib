namespace Coworkee.Application.Messaging;

public sealed class PostAttribute(string route) : EndpointAttribute("POST", route);
