using Beeexy.Domain.Common;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Tests.Integration.Infrastructure;

[Trait("Category", "Phase8Acceptance")]
public sealed class AvailabilityQuerySqlTests
{
    [Fact]
    public void MainQuery_UsesServerSideNotExistsPublicBoundaryAndStableOrdering()
    {
        using var dbContext = new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql("Host=localhost;Database=sql_shape;Username=test;Password=test")
                .Options);
        var repository = new AvailabilitySlotReadRepository(
            dbContext,
            new PublicDirectoryQueryBoundary(dbContext));
        var from = new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero);

        var sql = repository.BuildQuery(
            EntityId.From(Guid.Parse("82000000-0000-4000-8000-000000000001")),
            from,
            from.AddDays(30),
            from).ToQueryString();

        Assert.Contains("FROM scheduling.availability_slots", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("scheduling.appointments", sql, StringComparison.Ordinal);
        Assert.Contains("requested", sql, StringComparison.Ordinal);
        Assert.Contains("confirmed", sql, StringComparison.Ordinal);
        Assert.Contains("directory.doctor_affiliations", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("starts_at", sql, StringComparison.Ordinal);
        Assert.Contains("id", sql, StringComparison.Ordinal);
    }
}
