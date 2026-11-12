namespace Coworkee.Contracts.Ai;

/// <summary>
/// What the user sees in the client, sent with a message: the pages of the navigation the user may open, the current page, the appearance
/// choices and the client actions the client can run. The assistant uses it to explain where things are and to act in the UI.
/// <see cref="LocalSets"/> names the OData sets whose queries the browser answers from its local copy right now; the assistant's reads of them go there first.
/// </summary>
public sealed record AiUiContext(
    IReadOnlyList<AiUiPage>? Pages = null, AiUiPage? Current = null, IReadOnlyList<string>? Themes = null, string? Theme = null, string? Mode = null,
    IReadOnlyList<string>? Languages = null, string? Language = null, bool RightToLeft = false, IReadOnlyList<AiClientActionDto>? Actions = null,
    IReadOnlyList<string>? LocalSets = null);
