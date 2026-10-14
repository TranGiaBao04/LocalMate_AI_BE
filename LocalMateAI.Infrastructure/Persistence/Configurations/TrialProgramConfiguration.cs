using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class TrialProgramConfiguration : IEntityTypeConfiguration<TrialProgram>
{
    public void Configure(EntityTypeBuilder<TrialProgram> builder)
    {
        builder.ToTable("TrialPrograms", table =>
        {
            table.HasCheckConstraint("CK_TrialPrograms_Range", "\"StartsOn\" <= \"EndsOn\"");
            table.HasCheckConstraint("CK_TrialPrograms_TrialDays", "\"TrialDays\" > 0");
            table.HasCheckConstraint("CK_TrialPrograms_MaxRecipients", "\"MaxRecipients\" IS NULL OR \"MaxRecipients\" > 0");
        });

        builder.HasKey(program => program.Id);

        builder.Property(program => program.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(program => program.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
