using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.Notifications;

public sealed class NotificationQueryValidator : PagedQueryValidator<NotificationQuery>
{
    public NotificationQueryValidator() : base(NotificationQuery.SortFields)
    {
    }
}
