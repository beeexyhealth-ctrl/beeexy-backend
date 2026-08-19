using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Persistence;

public sealed class BeeexyDbContext(DbContextOptions<BeeexyDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BeeexyDbContext).Assembly);
    }
}
