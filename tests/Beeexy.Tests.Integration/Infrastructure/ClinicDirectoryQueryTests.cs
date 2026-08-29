using System.Data.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ClinicDirectoryQueryTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task List_AppliesVisibilityLocationFilterAndLimitInOneDatabaseQuery()
    {
        await EnsureImportedAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var repository = new ClinicDirectoryReadRepository(
            new PublicDirectoryQueryBoundary(dbContext));

        var items = await repository.ListAsync(
            new ClinicDirectoryFilter(
                null,
                "Demo Central",
                "Synthetic Demo Region",
                "Synthetic Demo Country"),
            null,
            2);

        Assert.Single(items);
        var command = Assert.Single(interceptor.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_published", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("locality", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("administrative_area", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("country", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_UsesOneClinicQueryAndOneLocationQueryWithoutPerLocationReads()
    {
        await EnsureImportedAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var repository = new ClinicDirectoryReadRepository(
            new PublicDirectoryQueryBoundary(dbContext));

        var detail = await repository.GetAsync(EntityId.From(Guid.Parse(
            "71020000-0000-4000-8000-000000000001")));

        Assert.NotNull(detail);
        Assert.Single(detail.Locations);
        Assert.Equal(2, interceptor.Commands.Count);
        Assert.All(interceptor.Commands, command =>
            Assert.Contains("is_published", command, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnsureImportedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await new DirectoryImporter(
                dbContext,
                new DirectoryImportPackageValidator(),
                NullLogger<DirectoryImporter>.Instance)
            .ImportAsync(ProductApprovedSyntheticDirectory.Create());
    }

    private BeeexyDbContext CreateDbContext(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new BeeexyDbContext(options.Options);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
