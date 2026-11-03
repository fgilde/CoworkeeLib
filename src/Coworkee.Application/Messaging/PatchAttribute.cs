namespace Coworkee.Application.Messaging;

public sealed class PatchAttribute(string route) : EndpointAttribute("PATCH", route);
