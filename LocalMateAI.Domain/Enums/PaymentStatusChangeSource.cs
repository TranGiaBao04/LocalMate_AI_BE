namespace LocalMateAI.Domain.Enums;

public enum PaymentStatusChangeSource
{
    Checkout,
    Webhook,
    ProviderLookup,
    LocalExpiration,
    AdminReconcile,
    BackgroundReconcile
}
