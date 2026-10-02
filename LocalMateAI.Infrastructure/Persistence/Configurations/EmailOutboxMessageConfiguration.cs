using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutboxMessages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.ToEmail)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(message => message.Subject)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(message => message.TemplateName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(message => message.Model)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(message => message.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(message => message.AttemptCount)
            .IsRequired();

        builder.Property(message => message.NextAttemptAt)
            .IsRequired();

        builder.Property(message => message.SentAt)
            .IsRequired(false);

        builder.Property(message => message.DeduplicationKey)
            .HasMaxLength(200)
            .IsRequired();

        // Enqueue dùng ON CONFLICT trên index này để không bao giờ tạo trùng mail.
        builder.HasIndex(message => message.DeduplicationKey)
            .IsUnique()
            .HasDatabaseName("UX_EmailOutboxMessages_DeduplicationKey");

        // Job chỉ tìm mail đang chờ theo thời điểm đến hạn.
        builder.HasIndex(message => message.NextAttemptAt)
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("IX_EmailOutboxMessages_NextAttemptAt_Pending");
    }
}
