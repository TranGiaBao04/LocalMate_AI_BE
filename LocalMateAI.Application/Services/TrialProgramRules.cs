using LocalMateAI.Application.DTOs.Subscription;

namespace LocalMateAI.Application.Services;

// Luật của chương trình dùng thử khi đăng ký (logic thuần, không chạm DB).
public static class TrialProgramRules
{
    public const int MaxNameLength = 100;
    public const int MinTrialDays = 1;
    public const int MaxTrialDays = 30;
    public const int MinRecipients = 1;
    public const int MaxRecipients = 100_000;

    // "Hôm nay" của chương trình tính theo giờ Việt Nam.
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(VietnamTime.Now(clock));

    public static TrialProgramStatus Status(TrialProgramReadModel program, DateOnly today)
    {
        if (!program.IsEnabled)
        {
            return TrialProgramStatus.Disabled;
        }

        if (today > program.EndsOn)
        {
            return TrialProgramStatus.Ended;
        }

        if (today < program.StartsOn)
        {
            return TrialProgramStatus.Upcoming;
        }

        return program.MaxRecipients is { } max && program.GrantedCount >= max
            ? TrialProgramStatus.Full
            : TrialProgramStatus.Running;
    }

    public static TrialProgramResponse ToResponse(TrialProgramReadModel program, DateOnly today) => new(
        program.Id,
        program.Name,
        program.StartsOn,
        program.EndsOn,
        program.TrialDays,
        program.MaxRecipients,
        program.IsEnabled,
        Status(program, today),
        program.GrantedCount,
        program.MaxRecipients is { } max ? Math.Max(0, max - program.GrantedCount) : null,
        program.CreatedAt,
        program.UpdatedAt);
}
