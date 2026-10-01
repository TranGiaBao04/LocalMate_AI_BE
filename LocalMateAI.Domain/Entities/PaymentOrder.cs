using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class PaymentOrder : BaseEntity
{
    public Guid UserId { get; set; }
    public PlanCode? PlanCode { get; set; }
    public Guid? PlanId { get; set; }
    public Guid? PlanVersionId { get; set; }
    public PlanVersionBinding PlanVersionBinding { get; set; }
    public PaymentOrderType Type { get; set; }
    public decimal Amount { get; set; }
    public PaymentOrderStatus Status { get; set; }
    public long ProviderOrderCode { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? QrCode { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
