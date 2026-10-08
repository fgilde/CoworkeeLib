namespace Coworkee.Application.Messaging;

/// <summary>
/// Offers a request to the AI assistant and MCP clients. Requests with <see cref="Authorization.RequiresPermissionAttribute"/> that return a
/// <see cref="Core.Results.Result"/> are offered without it; set <see cref="Exclude"/> to keep one away from the assistant.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AiToolAttribute(string? description = null) : Attribute
{
    public string? Description { get; } = description;

    /// <summary>Tool name; defaults to the type name in snake case without a Command or Query suffix.</summary>
    public string? Name { get; init; }

    public bool Exclude { get; init; }
}
