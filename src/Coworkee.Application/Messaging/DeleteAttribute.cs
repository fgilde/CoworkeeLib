namespace Coworkee.Application.Messaging;

public sealed class DeleteAttribute(string route) : EndpointAttribute("DELETE", route);
