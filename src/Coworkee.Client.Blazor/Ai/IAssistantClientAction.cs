using System.Text.Json;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>
/// A tool the assistant can call that runs in the browser, not on the server (open a page, switch the theme...). Register more with
/// services.AddScoped&lt;IAssistantClientAction, MyAction&gt;(); the client offers all of them with every message. They run without a
/// confirmation, so keep them to reversible UI changes and check the arguments: they come from the model.
/// </summary>
public interface IAssistantClientAction
{
    /// <summary>The tool name the model sees: letters, digits, _ and -, unique, and not the name of a server tool.</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>The JSON schema of the arguments, an object schema.</summary>
    JsonElement Parameters { get; }

    Task<AssistantActionResult> RunAsync(JsonElement arguments, CancellationToken cancellationToken);
}
