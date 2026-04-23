using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETmessenger.Domain.Entities;

namespace NETmessenger.Infrastructure.Persistence.Configurations;

public sealed class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEvent>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEvent> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.Outcome)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.Severity)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.IpAddress)
            .HasMaxLength(64);

        builder.Property(x => x.UserAgent)
            .HasMaxLength(512);

        builder.Property(x => x.ResourceType)
            .HasMaxLength(80);

        builder.Property(x => x.ResourceId)
            .HasMaxLength(128);

        builder.Property(x => x.Reason)
            .HasMaxLength(256);

        builder.Property(x => x.MetadataJson)
            .HasMaxLength(2000);

        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.EventType);
    }
}
