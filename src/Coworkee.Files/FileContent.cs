namespace Coworkee.Files;

public sealed record FileContent(string Name, string ContentType, Stream Content);
