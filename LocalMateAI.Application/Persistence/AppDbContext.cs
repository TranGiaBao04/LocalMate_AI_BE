using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Application.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
