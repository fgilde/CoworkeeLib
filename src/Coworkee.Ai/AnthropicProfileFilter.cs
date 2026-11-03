namespace Coworkee.Ai;

/// <summary>
/// The Anthropic SDK adds the workspace of the machine's Anthropic profile (%APPDATA%/anthropic, ANTHROPIC_CONFIG_DIR) to every request,
/// even with an explicit key; a key of another workspace then gets "Workspace … not found". The connection settings are the only source here.
/// </summary>
internal sealed class AnthropicProfileFilter : DelegatingHandler
{
    public const string WorkspaceHeader = "anthropic-workspace-id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(WorkspaceHeader);
        return base.SendAsync(request, cancellationToken);
    }
}
