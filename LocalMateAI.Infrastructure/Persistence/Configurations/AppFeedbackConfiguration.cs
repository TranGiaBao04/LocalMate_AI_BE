using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class AppFeedbackConfiguration : IEntityTypeConfiguration<AppFeedback>
{
    public void Configure(EntityTypeBuilder<AppFeedback> builder)
    {
        builder.ToTable("AppFeedbacks", table =>
        {
            table.HasCheckConstraint("CK_AppFeedbacks_Rating", "\"Rating\" IS NULL OR (\"Rating\" >= 1 AND \"Rating\" <= 5)");
        });

        builder.HasKey(f => f.Id);

        builder.Property(f => f.UserId)
            .IsRequired();

        builder.Property(f => f.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.Rating)
            .IsRequired(false);

        builder.Property(f => f.Content)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(f => f.PagePath)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(f => f.UserAgent)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.HasIndex(f => f.CreatedAt)
            .IsDescending()
            .HasDatabaseName("IX_AppFeedbacks_CreatedAt");

        builder.HasIndex(f => new { f.UserId, f.CreatedAt })
            .HasDatabaseName("IX_AppFeedbacks_UserId_CreatedAt");

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_AppFeedbacks_Users_UserId");
    }
}
