using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Persistence;

internal sealed class LocalizationModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Language>(language =>
        {
            language.ToTable("Languages", "cw");
            language.Property(l => l.Culture).HasMaxLength(20);
            language.Property(l => l.Name).HasMaxLength(100);
            language.HasIndex(l => l.Culture).IsUnique();
        });

        modelBuilder.Entity<Translation>(translation =>
        {
            translation.ToTable("Translations", "cw");
            translation.Property(t => t.Culture).HasMaxLength(20);
            translation.Property(t => t.Key).HasMaxLength(500);
            translation.HasIndex(t => new { t.Culture, t.Key }).IsUnique();
        });

        modelBuilder.Entity<TextKey>(key =>
        {
            key.ToTable("TextKeys", "cw");
            key.Property(k => k.Key).HasMaxLength(500);
            key.HasIndex(k => k.Key).IsUnique();
        });
    }
}
