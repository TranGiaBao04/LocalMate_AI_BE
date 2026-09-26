using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public static class SubscriptionModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.HasSequence<long>("PaymentOrderCodeSequence")
            .StartsAt(1)
            .IncrementsBy(1);
}
