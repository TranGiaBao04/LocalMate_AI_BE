using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class AppFeedback : BaseEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public AppFeedbackType Type { get; set; }

    public int? Rating { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? PagePath { get; set; }

    public string? UserAgent { get; set; }
}
