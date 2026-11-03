namespace Coworkee.Domain;

public interface IMultiTenant
{
    Guid TenantId { get; set; }
}
