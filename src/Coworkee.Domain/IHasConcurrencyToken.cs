namespace Coworkee.Domain;

public interface IHasConcurrencyToken
{
    uint Version { get; set; }
}
