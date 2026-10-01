using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Persistence;

public interface IModelContributor
{
    void Apply(ModelBuilder modelBuilder);
}
