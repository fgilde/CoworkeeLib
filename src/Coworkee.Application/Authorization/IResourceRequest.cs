namespace Coworkee.Application.Authorization;

public interface IResourceRequest
{
    string ResourceType { get; }

    Guid ResourceId { get; }

    string RequiredPermission { get; }
}
