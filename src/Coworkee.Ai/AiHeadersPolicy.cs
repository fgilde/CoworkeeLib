using System.ClientModel.Primitives;

namespace Coworkee.Ai;

/// <summary>Adds the extra headers of a connection and, with an auth header name, sends the key there instead of as bearer token.</summary>
internal sealed class AiHeadersPolicy(AiConnection connection) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message.Request);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message.Request);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Apply(PipelineRequest request)
    {
        if (connection.AuthHeader is { Length: > 0 })
        {
            request.Headers.Remove("Authorization");
        }

        foreach (var (name, value) in AiEndpoints.Headers(connection))
        {
            request.Headers.Set(name, value);
        }
    }
}
