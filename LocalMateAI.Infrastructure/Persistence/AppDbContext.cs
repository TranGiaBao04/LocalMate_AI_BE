using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider? timeProvider = null) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<MetroStation> MetroStations => Set<MetroStation>();
    public DbSet<Place> Places => Set<Place>();
    public DbSet<CuratedItinerary> CuratedItineraries => Set<CuratedItinerary>();
    public DbSet<CuratedItineraryItem> CuratedItineraryItems => Set<CuratedItineraryItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<UserPreferenceTag> UserPreferenceTags => Set<UserPreferenceTag>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => Set<SubscriptionPlanVersion>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<SubscriptionPlanVersionFeature> SubscriptionPlanVersionFeatures => Set<SubscriptionPlanVersionFeature>();
    public DbSet<SubscriptionPeriod> SubscriptionPeriods => Set<SubscriptionPeriod>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<PaymentOrderCredit> PaymentOrderCredits => Set<PaymentOrderCredit>();
    public DbSet<SingleItineraryProductVersion> SingleItineraryProductVersions => Set<SingleItineraryProductVersion>();
    public DbSet<SingleItineraryEntitlement> SingleItineraryEntitlements => Set<SingleItineraryEntitlement>();
    public DbSet<PaymentWebhookReceipt> PaymentWebhookReceipts => Set<PaymentWebhookReceipt>();
    public DbSet<PaymentOrderStatusHistory> PaymentOrderStatusHistories => Set<PaymentOrderStatusHistory>();
    public DbSet<EntitlementRepairAudit> EntitlementRepairAudits => Set<EntitlementRepairAudit>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<PlaceReview> PlaceReviews => Set<PlaceReview>();
    public DbSet<TripTag> TripTags => Set<TripTag>();
    public DbSet<PlaceTag> PlaceTags => Set<PlaceTag>();
    public DbSet<PlaceImage> PlaceImages => Set<PlaceImage>();
        public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<EmailOtpCode> EmailOtpCodes => Set<EmailOtpCode>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tìm kiếm không dấu (unaccent(...) ILIKE unaccent(...)), dùng ở danh sách user admin (BE-132).
        modelBuilder.HasPostgresExtension("unaccent");
        SubscriptionModelConfiguration.Configure(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAudit();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAudit();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAudit()
    {
        foreach (var entry in ChangeTracker.Entries<PaymentOrder>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Entity.CreditAmount < 0 || entry.Entity.CreditAmount != decimal.Truncate(entry.Entity.CreditAmount)
                || (entry.Entity.CreditAmount != 0 &&
                    (entry.Entity.Type != LocalMateAI.Domain.Enums.PaymentOrderType.Upgrade
                     || entry.Entity.ProductKind != LocalMateAI.Domain.Enums.PaymentProductKind.SubscriptionPlan))
                || (entry.State == EntityState.Modified &&
                    entry.Properties.Any(p => p.IsModified && p.Metadata.Name == nameof(PaymentOrder.CreditAmount))))
                throw new InvalidOperationException("Credit must be an immutable whole-VND SubscriptionPlan Upgrade snapshot.");
        }
        foreach (var entry in ChangeTracker.Entries<PaymentOrderCredit>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Credit evidence cannot be deleted.");
            if (entry.State == EntityState.Added &&
                (entry.Entity.ReleasedAt is not null || entry.Entity.HasAnyReleaseEvidence() || entry.Entity.OriginalEndsAt.Kind != DateTimeKind.Utc
                 || entry.Entity.RemainingDays < 0 || entry.Entity.CalculatedCreditAmount < 0
                 || entry.Entity.CalculatedCreditAmount != decimal.Truncate(entry.Entity.CalculatedCreditAmount)))
                throw new InvalidOperationException("Credit snapshots require UTC dates and non-negative whole-VND values.");
            if (entry.State == EntityState.Modified &&
                (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not
                    (nameof(PaymentOrderCredit.ReleasedAt) or nameof(PaymentOrderCredit.ReleaseProviderCheckedAt)
                     or nameof(PaymentOrderCredit.ReleaseProviderStatus) or nameof(PaymentOrderCredit.ReleaseProviderRequestedAmount)
                     or nameof(PaymentOrderCredit.ReleaseProviderAmountPaid) or nameof(PaymentOrderCredit.ReleaseProviderAmountRemaining)
                     or nameof(PaymentOrderCredit.ReleaseReasonCode)))
                 || entry.OriginalValues.GetValue<DateTime?>(nameof(PaymentOrderCredit.ReleasedAt)) is not null
                 || !entry.Entity.HasValidReleaseEvidence()))
                throw new InvalidOperationException("Credit evidence is immutable; release is a one-way UTC transition.");
        }
        foreach (var entry in ChangeTracker.Entries<SubscriptionPeriod>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Entitlement periods cannot be deleted.");
            if (entry.State == EntityState.Added &&
                (entry.Entity.TerminatedAt is not null || entry.Entity.TerminatedByOrderId is not null))
                throw new InvalidOperationException("New periods cannot be pre-terminated.");
            if (entry.State == EntityState.Modified &&
                (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not
                    (nameof(SubscriptionPeriod.TerminatedAt) or nameof(SubscriptionPeriod.TerminatedByOrderId)))
                 || entry.OriginalValues.GetValue<DateTime?>(nameof(SubscriptionPeriod.TerminatedAt)) is not null
                 || entry.OriginalValues.GetValue<Guid?>(nameof(SubscriptionPeriod.TerminatedByOrderId)) is not null
                 || entry.Entity.TerminatedAt is not { Kind: DateTimeKind.Utc }
                 || entry.Entity.TerminatedByOrderId is null || entry.Entity.TerminatedByOrderId == Guid.Empty))
                throw new InvalidOperationException("Period evidence is immutable; only one-way Upgrade termination is allowed.");
        }
        if (ChangeTracker.Entries<SingleItineraryProductVersion>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<SingleItineraryProductVersion>().Any(e => e.State == EntityState.Added &&
                (e.Entity.Price <= 0 || e.Entity.Price != decimal.Truncate(e.Entity.Price) || e.Entity.VersionNumber <= 0)))
            throw new InvalidOperationException("Single itinerary contracts are immutable positive whole-VND versions.");
        foreach (var entry in ChangeTracker.Entries<SingleItineraryEntitlement>())
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Entitlement evidence cannot be deleted.");
            if (entry.State != EntityState.Modified) continue;
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not
                    (nameof(SingleItineraryEntitlement.ConsumedAt) or nameof(SingleItineraryEntitlement.ConsumedTripId)))
                || entry.OriginalValues.GetValue<DateTime?>(nameof(SingleItineraryEntitlement.ConsumedAt)) is not null
                || entry.OriginalValues.GetValue<Guid?>(nameof(SingleItineraryEntitlement.ConsumedTripId)) is not null
                || entry.Entity.ConsumedAt is null || entry.Entity.ConsumedTripId is null
                || entry.Entity.ConsumedAt < entry.Entity.GrantedAt)
                throw new InvalidOperationException("Entitlement provenance is immutable and consumption is one-way.");
        }
        foreach (var entry in ChangeTracker.Entries<PaymentOrder>().Where(e => e.State == EntityState.Modified))
        {
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name == nameof(PaymentOrder.ProductKind)) ||
                (entry.Entity.ProductKind == LocalMateAI.Domain.Enums.PaymentProductKind.SingleItinerary &&
                entry.Properties.Any(p => p.IsModified && p.Metadata.Name is
                    nameof(PaymentOrder.UserId) or nameof(PaymentOrder.Amount) or nameof(PaymentOrder.ProviderOrderCode)
                    or nameof(PaymentOrder.SingleItineraryProductVersionId) or nameof(PaymentOrder.CheckoutAttemptId)
                    or nameof(PaymentOrder.Type) or nameof(PaymentOrder.PlanCode) or nameof(PaymentOrder.PlanId)
                    or nameof(PaymentOrder.PlanVersionId) or nameof(PaymentOrder.PlanVersionBinding))))
                throw new InvalidOperationException("Purchased product snapshot is immutable.");
        }
        if (ChangeTracker.Entries<EntitlementRepairAudit>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Entitlement repair audits are insert-only.");
        if (ChangeTracker.Entries<PaymentOrderStatusHistory>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Payment status history is insert-only.");
        foreach (var entry in ChangeTracker.Entries<PaymentWebhookReceipt>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Payment webhook receipts cannot be deleted.");
            if (entry.State != EntityState.Modified) continue;
            var allowed = new[] { nameof(PaymentWebhookReceipt.RawPayload), nameof(PaymentWebhookReceipt.RawPayloadPurgedAt) };
            if (entry.Properties.Any(p => p.IsModified && !allowed.Contains(p.Metadata.Name))
                || entry.OriginalValues.GetValue<string?>(nameof(PaymentWebhookReceipt.RawPayload)) is null
                || entry.OriginalValues.GetValue<DateTime?>(nameof(PaymentWebhookReceipt.RawPayloadPurgedAt)) is not null
                || entry.Entity.RawPayload is not null
                || entry.Entity.RawPayloadPurgedAt is null
                || entry.Entity.RawPayloadPurgedAt < entry.Entity.RawPayloadRetainUntil
                || (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime < entry.Entity.RawPayloadRetainUntil)
                throw new InvalidOperationException("Webhook metadata is immutable; only expired raw payload can be purged.");
        }
        if (ChangeTracker.Entries().Any(e =>
            (e.Entity is SubscriptionPlanVersion or SubscriptionPlanVersionFeature)
            && e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Published versions, their features and entitlement periods are immutable.");

        var newVersions = ChangeTracker.Entries<SubscriptionPlanVersion>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity.Id).ToHashSet();
        if (ChangeTracker.Entries<SubscriptionPlanVersionFeature>().Any(e =>
            e.State == EntityState.Added && !newVersions.Contains(e.Entity.PlanVersionId)))
            throw new InvalidOperationException("Features must be added together with a new plan version.");

        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified && entry.Entity is not SubscriptionPeriod)
            {
                entry.Property(entity => entity.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
