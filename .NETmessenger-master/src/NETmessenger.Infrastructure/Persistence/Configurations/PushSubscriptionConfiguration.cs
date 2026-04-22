using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETmessenger.Domain.Entities;

namespace NETmessenger.Infrastructure.Persistence.Configurations;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Endpoint)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(x => x.P256dh)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.Auth)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.UserAgent)
            .HasMaxLength(512);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.FailureCount)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasIndex(x => x.Endpoint)
            .IsUnique();

        builder.HasIndex(x => x.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

