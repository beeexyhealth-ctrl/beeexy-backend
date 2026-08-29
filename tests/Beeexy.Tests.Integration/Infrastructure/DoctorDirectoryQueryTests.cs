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
public sealed class DoctorDirectoryQueryTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Search_AppliesAllFiltersVisibilityOrderingAndLimitBeforeBulkProjection()
    {
        await EnsureImportedAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var repository = new DoctorDirectoryReadRepository(
            new PublicDirectoryQueryBoundary(dbContext));

        var items = await repository.SearchAsync(
            new DoctorDirectoryFilter(
                "demo-specialty-general",
                "demo-language-es",
                "Demo Harbor",
                "Synthetic Demo Region",
                "Synthetic Demo Country",
                "demo-plan-blue"),
            null,
            2);

        Assert.Equal("demo-doctor-blue", Assert.Single(items).Code);
        Assert.Equal(6, interceptor.Commands.Count);
        var doctorQuery = interceptor.Commands[0];
        Assert.Contains("LIMIT", doctorQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", doctorQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_published", doctorQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doctor_specialties", doctorQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doctor_languages", doctorQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doctor_insurance_participations", doctorQuery,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doctor_affiliations", doctorQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clinic_locations", doctorQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", doctorQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_UsesFixedBulkQueriesAndVisibilityBoundaryForEveryNestedCollection()
    {
        await EnsureImportedAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var repository = new DoctorDirectoryReadRepository(
            new PublicDirectoryQueryBoundary(dbContext));

        var detail = await repository.GetAsync(EntityId.From(Guid.Parse(
            "71020000-0000-4200-8000-000000000021")));

        Assert.NotNull(detail);
        Assert.Single(detail.Affiliations);
        Assert.Single(detail.Credentials);
        Assert.Equal(6, interceptor.Commands.Count);
        Assert.All(interceptor.Commands, command =>
            Assert.Contains("doctors", command, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(interceptor.Commands, command =>
            command.Contains("status", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("verified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_NoMatchExecutesOnlyBoundedDoctorQueryWithoutRelationshipReads()
    {
        await EnsureImportedAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var repository = new DoctorDirectoryReadRepository(
            new PublicDirectoryQueryBoundary(dbContext));

        var items = await repository.SearchAsync(
            new DoctorDirectoryFilter(
                "demo-specialty-not-present",
                null,
                null,
                null,
                null,
                null),
            null,
            21);

        Assert.Empty(items);
        var command = Assert.Single(interceptor.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doctor_specialties", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RankedSearch_AppliesHardFiltersBeforeExistingEngineAndBulkLoadsOnlyPage()
    {
        await EnsureImportedAsync();
        await using (var importContext = CreateDbContext())
        {
            await new DoctorMatchRuleImporter(
                    importContext,
                    new DoctorMatchRulePackageValidator(),
                    NullLogger<DoctorMatchRuleImporter>.Instance)
                .ImportAsync(ProductApprovedDemoDoctorMatchRule.Create());
        }

        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var boundary = new PublicDirectoryQueryBoundary(dbContext);
        var directoryRepository = new DoctorDirectoryReadRepository(boundary);
        var useCase = new SearchDoctors(
            directoryRepository,
            new CalculateDoctorMatch(
                new DoctorMatchingRepository(dbContext, boundary),
                new DeterministicDoctorMatchEngine()));

        var result = await useCase.ExecuteAsync(new SearchDoctorsQuery(
            PageSize: 1,
            SpecialtyCode: "demo-specialty-general",
            LanguageCode: "demo-language-es",
            Locality: "Demo Harbor",
            AdministrativeArea: "Synthetic Demo Region",
            Country: "Synthetic Demo Country",
            InsurancePlanCode: "demo-plan-blue"));

        var item = Assert.Single(result.Items);
        Assert.Equal("demo-doctor-blue", item.Profile.Code);
        Assert.Equal(100, item.Match!.MatchScore);
        Assert.All(item.Match.Factors, factor =>
            Assert.Equal(DoctorMatchFactorState.Matched, factor.State));
        Assert.Null(result.NextCursor);
        Assert.Equal(13, interceptor.Commands.Count);
        Assert.Contains("doctor_specialties", interceptor.Commands[0],
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", interceptor.Commands[7],
            StringComparison.OrdinalIgnoreCase);
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
