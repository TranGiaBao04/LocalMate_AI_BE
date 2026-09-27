using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MetroStation> MetroStations => Set<MetroStation>();
    public DbSet<Place> Places => Set<Place>();
    public DbSet<CuratedItinerary> CuratedItineraries => Set<CuratedItinerary>();
    public DbSet<CuratedItineraryItem> CuratedItineraryItems => Set<CuratedItineraryItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<UserPreferenceTag> UserPreferenceTags => Set<UserPreferenceTag>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<PlaceReview> PlaceReviews => Set<PlaceReview>();
    public DbSet<TripTag> TripTags => Set<TripTag>();
        public DbSet<PlaceTag> PlaceTags => Set<PlaceTag>();
        public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<EmailOtpCode> EmailOtpCodes => Set<EmailOtpCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(entity => entity.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
