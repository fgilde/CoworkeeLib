using Coworkee.Ai;
using Coworkee.Application.Retention;
using Coworkee.AuthServer;
using Coworkee.Backup;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.EventBus;
using Coworkee.Files;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Retention.Tests;

[DependsOn(typeof(CoworkeeRetentionModule), typeof(CoworkeeIdentityModule), typeof(CoworkeeWebhooksModule), typeof(CoworkeeFilesModule), typeof(CoworkeeBackupModule),
    typeof(CoworkeeEventBusModule), typeof(CoworkeeAiModule), typeof(CoworkeeAuthStoreModule), typeof(Notifications.CoworkeeNotificationsModule), typeof(Mailing.CoworkeeMailingModule),
    typeof(OData.CoworkeeODataModule))]
public sealed class TestRetentionModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, GadgetModel>();
        context.Services.AddRetentionOwnedType<Phone>();
        context.Services.AddCoworkeeDbContext<RetentionTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
    }
}

public sealed class RetentionTestDbContext(DbContextOptions<RetentionTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

public sealed class Gadget : Entity, ISoftDelete
{
    public required string Name { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

/// <summary>Soft deleted owners with pets cannot go: the pets hold them.</summary>
public sealed class Owner : Entity, ISoftDelete
{
    public required string Name { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

public sealed class Pet : Entity
{
    public Guid OwnerId { get; set; }
}

/// <summary>A table per hierarchy: devices go with soft-deleted, phones are left to another target.</summary>
public class Device : Entity, ISoftDelete
{
    public required string Name { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

public sealed class Phone : Device;

/// <summary>A root that is not soft deleted with a type below it that is.</summary>
public class Vehicle : Entity
{
    public required string Name { get; set; }
}

public sealed class Car : Vehicle, ISoftDelete
{
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

internal sealed class GadgetModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Gadget>(gadget => gadget.ToTable("Gadgets"));
        modelBuilder.Entity<Owner>(owner => owner.ToTable("Owners"));
        modelBuilder.Entity<Device>(device => device.ToTable("Devices"));
        modelBuilder.Entity<Phone>();
        modelBuilder.Entity<Vehicle>(vehicle => vehicle.ToTable("Vehicles"));
        modelBuilder.Entity<Car>();
        modelBuilder.Entity<Pet>(pet =>
        {
            pet.ToTable("Pets");
            pet.HasOne<Owner>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
