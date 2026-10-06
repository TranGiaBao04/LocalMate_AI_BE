using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class AiUsageAdmissionConfiguration : IEntityTypeConfiguration<AiUsageAdmission>
{
    public void Configure(EntityTypeBuilder<AiUsageAdmission> builder)
    {
        builder.ToTable("AiUsageAdmissions", table =>
        {
            table.HasCheckConstraint("CK_AiAdmissions_Kind", "\"Kind\" IN ('ParseRequest','Explain')");
            table.HasCheckConstraint("CK_AiAdmissions_Terms", "\"AdmittedDailyLimit\" BETWEEN 0 AND 1000 AND "
                + "((\"Kind\"='ParseRequest' AND \"TripId\" IS NULL AND \"TripIdSnapshot\" IS NULL AND \"AdmittedExplainLimit\" IS NULL) OR "
                + "(\"Kind\"='Explain' AND \"TripIdSnapshot\" IS NOT NULL AND \"AdmittedExplainLimit\" IS NOT NULL AND \"AdmittedExplainLimit\" BETWEEN 0 AND 20))");
            table.HasCheckConstraint("CK_AiAdmissions_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND "
                + "\"FencingToken\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"FencingGeneration\" > 0 AND "
                + "(\"TripId\" IS NULL OR \"TripId\"=\"TripIdSnapshot\")");
            table.HasCheckConstraint("CK_AiAdmissions_Time", "\"ReservedUntil\"=\"AdmittedAt\" + interval '30 seconds' AND "
                + "\"VietnamUsageDate\"=((\"AdmittedAt\" AT TIME ZONE 'UTC') + interval '7 hours')::date AND "
                + "(\"DispatchAuthorizedAt\" IS NULL OR \"DispatchAuthorizedAt\">=\"AdmittedAt\") AND "
                + "(\"CompletedAt\" IS NULL OR \"CompletedAt\">=COALESCE(\"DispatchAuthorizedAt\",\"AdmittedAt\")) AND "
                + "(\"RecoveryAfter\" IS NULL OR \"RecoveryAfter\">=\"DispatchAuthorizedAt\")");
            table.HasCheckConstraint("CK_AiAdmissions_State", ""
                + "(\"State\"='Reserved' AND \"DispatchAuthorizedAt\" IS NULL AND \"CompletedAt\" IS NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL AND \"RecoveryAfter\" IS NULL) OR "
                + "(\"State\"='DispatchAuthorized' AND \"DispatchAuthorizedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL) OR "
                + "(\"State\"='Completed' AND \"DispatchAuthorizedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Succeeded','ProviderFailed','InvalidOutput') AND \"LlmCallLogId\" IS NOT NULL AND \"RecoveryAfter\" IS NULL) OR "
                + "(\"State\"='Released' AND \"DispatchAuthorizedAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL AND \"RecoveryAfter\" IS NULL) OR "
                + "(\"State\"='Abandoned' AND \"DispatchAuthorizedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL AND \"RecoveryAfter\" IS NULL)");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.VietnamUsageDate).HasColumnType("date");
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Trip>().WithMany().HasForeignKey(a => a.TripId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SubscriptionPlanVersion>().WithMany().HasForeignKey(a => a.ResolvedPlanVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LlmCallLog>().WithMany().HasForeignKey(a => a.LlmCallLogId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.UserId, a.VietnamUsageDate, a.State });
        builder.HasIndex(a => new { a.TripIdSnapshot, a.Kind, a.State });
        builder.HasIndex(a => new { a.ReservedUntil, a.State });
        builder.HasIndex(a => new { a.RecoveryAfter, a.State }).HasFilter("\"RecoveryAfter\" IS NOT NULL");
        builder.HasIndex(a => a.LlmCallLogId).IsUnique().HasFilter("\"LlmCallLogId\" IS NOT NULL");
    }
}
