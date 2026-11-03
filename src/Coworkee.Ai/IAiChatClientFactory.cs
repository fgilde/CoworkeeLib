using Microsoft.Extensions.AI;

namespace Coworkee.Ai;

/// <summary>Creates the chat client of a connection. Replace it to bring another <see cref="IChatClient"/> (Bedrock, a gateway with its own SDK).</summary>
public interface IAiChatClientFactory
{
    IChatClient Create(AiConnection connection);
}
