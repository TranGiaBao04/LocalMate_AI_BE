using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class EmailOtpCodeConfiguration : IEntityTypeConfiguration<EmailOtpCode>
{
    public void Configure(EntityTypeBuilder<EmailOtpCode> builder)
    {
        builder.ToTable("EmailOtpCodes");

        builder.HasKey(otp => otp.Id);

        builder.Property(otp => otp.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(otp => otp.Purpose)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(otp => otp.CodeHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(otp => otp.ExpiresAt)
            .IsRequired();

        builder.Property(otp => otp.AttemptCount)
            .IsRequired();

        builder.Property(otp => otp.ConsumedAt)
            .IsRequired(false);

        // Mỗi (email, mục đích) chỉ có 1 mã còn hiệu lực — DB chặn khi 2 request gửi mã cùng lúc.
        builder.HasIndex(otp => new { otp.Email, otp.Purpose })
            .IsUnique()
            .HasFilter("\"ConsumedAt\" IS NULL")
            .HasDatabaseName("UX_EmailOtpCodes_Email_Purpose_Active");

        // Đếm số lần gửi trong 1 giờ và lấy mã gần nhất để kiểm tra thời gian chờ 60 giây.
        builder.HasIndex(otp => new { otp.Email, otp.Purpose, otp.CreatedAt })
            .HasDatabaseName("IX_EmailOtpCodes_Email_Purpose_CreatedAt");
    }
}
