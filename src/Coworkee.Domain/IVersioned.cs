namespace Coworkee.Domain;

public interface IVersioned
{
    int Revision { get; set; }
}
