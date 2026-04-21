using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETmessenger.Domain.Entities;

namespace NETmessenger.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Text)
            .IsRequired(false);

        builder.Property(m => m.Type)
            .IsRequired();

        builder.Property(m => m.AudioUrl)
            .IsRequired(false);

        builder.Property(m => m.AudioContentType)
            .IsRequired(false);

        builder.Property(m => m.AudioDurationSeconds)
            .IsRequired(false);

        builder.Property(m => m.AudioSizeBytes)
            .IsRequired(false);

        builder.Property(m => m.MediaUrl)
            .IsRequired(false);

        builder.Property(m => m.MediaContentType)
            .IsRequired(false);

        builder.Property(m => m.MediaFileName)
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(m => m.MediaSizeBytes)
            .IsRequired(false);

        builder.HasOne(m => m.Sender)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Chat)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ChatId, m.SentAt });
    }
}
