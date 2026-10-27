using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubscriptionBilling.Domain.Plans;
using SubscriptionBilling.Domain.Subscriptions;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(User.FullNameMaxLength).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Unique among live accounts only: a soft-deleted account frees its email.
        builder.HasIndex(x => x.Email).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Plan.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Interval).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PlanName).HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Interval).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Referential integrity without navigations: the domain model references other aggregates by id only.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });

        // The database, not the application, guarantees "one active subscription per user", even under concurrent requests.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("UX_Subscriptions_OneActivePerUser")
            .HasFilter("[Status] = 'Active' AND [IsDeleted] = 0");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
