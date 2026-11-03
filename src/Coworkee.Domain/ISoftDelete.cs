namespace Coworkee.Domain;

public interface ISoftDelete
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }

    Guid? DeletedBy { get; set; }
}
