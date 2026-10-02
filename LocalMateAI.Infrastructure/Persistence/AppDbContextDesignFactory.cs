using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace LocalMateAI.Infrastructure.Persistence;
// Design tools never start the API, seed development users, or load private application configuration.
public sealed class AppDbContextDesignFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("LOCALMATE_TEST_CONNECTION")
            ?? "Host=localhost;Database=localmate_schema_design;Username=design",
            o => o.UseNetTopologySuite()).Options);
}
