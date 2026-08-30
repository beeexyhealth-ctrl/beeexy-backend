using System.Data.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class Phase77DirectoryQueryAcceptanceTests(PostgreSqlContainerFixture postgres)
{
    private static readonly EntityId AmberDoctorId = EntityId.From(Guid.Parse(
        "71020000-0000-4200-8000-000000000021"));

    [Fact]
    public async Task RepresentativeSearchShapes_ArePredicatedParameterizedAndExplainable()
    {
        await EnsureImportedAsync();

        var clinicNoFilter = await CaptureClinicListAsync(
            new ClinicDirectoryFilter(null, null, null, null));
        AssertBoundedQuery(clinicNoFilter, "clinics");

        var clinicLocation = await CaptureClinicListAsync(new ClinicDirectoryFilter(
            null,
            "Demo Central",
            "Synthetic Demo Region",
            "Synthetic Demo Country"));
        AssertBoundedQuery(clinicLocation, "clinics", "clinic_locations", "EXISTS");
        AssertValuesAreParameters(
            clinicLocation,
            "Demo Central",
            "Synthetic Demo Region",
            "Synthetic Demo Country");

        var doctorNoCriteria = await CaptureDoctorSearchAsync(EmptyDoctorFilter());
        AssertBoundedQuery(doctorNoCriteria, "doctors");

        var specialty = await CaptureDoctorSearchAsync(new DoctorDirectoryFilter(
            "demo-specialty-general", null, null, null, null, null));
        AssertBoundedQuery(specialty, "doctor_specialties", "specialties", "EXISTS");
        AssertValuesAreParameters(specialty, "demo-specialty-general");

        var language = await CaptureDoctorSearchAsync(new DoctorDirectoryFilter(
            null, "demo-language-es", null, null, null, null));
        AssertBoundedQuery(language, "doctor_languages", "languages", "EXISTS");
        AssertValuesAreParameters(language, "demo-language-es");

        var location = await CaptureDoctorSearchAsync(new DoctorDirectoryFilter(
            null,
            null,
            "Demo Harbor",
            "Synthetic Demo Region",
            "Synthetic Demo Country",
            null));
        AssertBoundedQuery(
            location,
            "doctor_affiliations",
            "clinic_locations",
            "clinics",
            "EXISTS");
        AssertValuesAreParameters(
            location,
            "Demo Harbor",
            "Synthetic Demo Region",
            "Synthetic Demo Country");

        var insurance = await CaptureDoctorSearchAsync(new DoctorDirectoryFilter(
            null, null, null, null, null, "demo-plan-blue"));
        AssertBoundedQuery(
            insurance,
            "doctor_insurance_participations",
            "insurance_plans",
            "EXISTS");
        AssertValuesAreParameters(insurance, "demo-plan-blue");

        var combinedFilter = new DoctorDirectoryFilter(
            "demo-specialty-general",
            "demo-language-es",
            "Demo Harbor",
            "Synthetic Demo Region",
            "Synthetic Demo Country",
            "demo-plan-blue");
        var combined = await CaptureDoctorSearchAsync(combinedFilter);
        AssertBoundedQuery(
            combined,
            "doctor_specialties",
            "doctor_languages",
            "doctor_affiliations",
            "clinic_locations",
            "doctor_insurance_participations");
        AssertValuesAreParameters(
            combined,
            "demo-specialty-general",
            "demo-language-es",
            "Demo Harbor",
            "Synthetic Demo Region",
            "Synthetic Demo Country",
            "demo-plan-blue");

        var continuation = await CaptureDoctorSearchAsync(
            EmptyDoctorFilter(),
            new DoctorDirectoryPageCursor(EmptyDoctorFilter(), AmberDoctorId));
        AssertBoundedQuery(continuation, "directory.doctors", ">");
        Assert.Contains(
            continuation.Parameters,
            parameter => Equals(parameter.Value, AmberDoctorId.Value));

        var ranked = await CaptureRankedSearchAsync();
        Assert.Equal(13, ranked.ActivePage.Count);
        Assert.Equal(13, ranked.ContinuationPage.Count);
        Assert.Contains("doctor_languages", ranked.ActivePage[0].Text,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", ranked.ActivePage[0].Text,
            StringComparison.OrdinalIgnoreCase);
        AssertValuesAreParameters(ranked.ActivePage[0], "demo-language-es");
        AssertValuesAreParameters(ranked.ContinuationPage[0], "demo-language-es");

        var representativePlans = new[]
        {
            clinicNoFilter,
            clinicLocation,
            doctorNoCriteria,
            specialty,
            language,
            location,
            insurance,
            combined,
            continuation,
            ranked.ActivePage[0],
            ranked.ContinuationPage[0]
        };
        foreach (var query in representativePlans)
        {
            await AssertExplainableAsync(query);
        }
    }

    [Fact]
    public async Task DirectoryIndexes_CoverPublicationFilteringRelationshipsAndCredentials()
    {
        await EnsureImportedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexname, indexdef FROM pg_indexes " +
            "WHERE schemaname = 'directory' ORDER BY indexname;";

        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            definitions.Add(reader.GetString(0), reader.GetString(1));
        }

        AssertIndex(definitions, "ix_clinics_published", "is_published");
        AssertIndex(definitions, "ix_doctors_published", "is_published");
        AssertIndex(
            definitions,
            "ix_clinic_locations_area_published",
            "country",
            "administrative_area",
            "locality",
            "is_published");
        AssertIndex(
            definitions,
            "ix_clinic_locations_clinic_published",
            "clinic_id",
            "is_published");
        AssertIndex(
            definitions,
            "ix_doctor_affiliations_doctor_published",
            "doctor_id",
            "is_published");
        AssertIndex(
            definitions,
            "ix_doctor_affiliations_clinic_published",
            "clinic_id",
            "is_published");
        AssertIndex(
            definitions,
            "ix_doctor_affiliations_clinic_location",
            "clinic_id",
            "clinic_location_id");
        AssertIndex(definitions, "ix_doctor_specialties_specialty_id", "specialty_id");
        AssertIndex(definitions, "ix_doctor_languages_language_id", "language_id");
        AssertIndex(definitions, "ix_doctor_insurance_plan_id", "insurance_plan_id");
        AssertIndex(
            definitions,
            "ix_doctor_credentials_doctor_status",
            "doctor_id",
            "status");
    }

    private async Task<CapturedCommand> CaptureClinicListAsync(ClinicDirectoryFilter filter)
    {
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var repository = new ClinicDirectoryReadRepository(
            new PublicDirectoryQueryBoundary(dbContext));

        await repository.ListAsync(filter, null, 2);

        return interceptor.Commands[0];
    }

    private async Task<CapturedCommand> CaptureDoctorSearchAsync(
        DoctorDirectoryFilter filter,
        DoctorDirectoryPageCursor? after = null)
    {
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var repository = new DoctorDirectoryReadRepository(
            new PublicDirectoryQueryBoundary(dbContext));

        await repository.SearchAsync(filter, after, 2);

        return interceptor.Commands[0];
    }

    private async Task<RankedSearchCapture> CaptureRankedSearchAsync()
    {
        var interceptor = new CommandCaptureInterceptor();
        await using var dbContext = CreateDbContext(interceptor);
        var boundary = new PublicDirectoryQueryBoundary(dbContext);
        var useCase = new SearchDoctors(
            new DoctorDirectoryReadRepository(boundary),
            new CalculateDoctorMatch(
                new DoctorMatchingRepository(dbContext, boundary),
                new DeterministicDoctorMatchEngine()));

        var first = await useCase.ExecuteAsync(new SearchDoctorsQuery(
            PageSize: 1,
            LanguageCode: "demo-language-es"));
        Assert.NotNull(first.NextCursor);
        var activePageCommands = interceptor.Commands.ToArray();

        interceptor.Commands.Clear();
        var second = await useCase.ExecuteAsync(new SearchDoctorsQuery(
            Cursor: first.NextCursor,
            PageSize: 1,
            LanguageCode: "demo-language-es"));
        Assert.Single(second.Items);

        return new RankedSearchCapture(
            activePageCommands,
            interceptor.Commands.ToArray());
    }

    private async Task AssertExplainableAsync(CapturedCommand captured)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN (FORMAT JSON) " + captured.Text;
        foreach (var parameter in captured.Parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(
                parameter.Name,
                parameter.Value ?? DBNull.Value));
        }

        var plan = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Contains("\"Plan\"", plan, StringComparison.Ordinal);
    }

    private static void AssertBoundedQuery(
        CapturedCommand command,
        params string[] expectedFragments)
    {
        Assert.Contains("is_published", command.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", command.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(command.Parameters);
        Assert.All(expectedFragments, fragment =>
            Assert.Contains(fragment, command.Text, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertValuesAreParameters(
        CapturedCommand command,
        params string[] expectedValues)
    {
        foreach (var value in expectedValues)
        {
            Assert.DoesNotContain(value, command.Text, StringComparison.Ordinal);
            Assert.Contains(command.Parameters, parameter => Equals(parameter.Value, value));
        }
    }

    private static void AssertIndex(
        IReadOnlyDictionary<string, string> definitions,
        string name,
        params string[] columns)
    {
        var definition = definitions[name];
        Assert.All(columns, column =>
            Assert.Contains(column, definition, StringComparison.OrdinalIgnoreCase));
    }

    private static DoctorDirectoryFilter EmptyDoctorFilter() =>
        new(null, null, null, null, null, null);

    private async Task EnsureImportedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await new DirectoryImporter(
                dbContext,
                new DirectoryImportPackageValidator(),
                NullLogger<DirectoryImporter>.Instance)
            .ImportAsync(ProductApprovedSyntheticDirectory.Create());
        await new DoctorMatchRuleImporter(
                dbContext,
                new DoctorMatchRulePackageValidator(),
                NullLogger<DoctorMatchRuleImporter>.Instance)
            .ImportAsync(ProductApprovedDemoDoctorMatchRule.Create());
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
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new CapturedCommand(
                command.CommandText,
                command.Parameters.Cast<DbParameter>()
                    .Select(parameter => new CapturedParameter(
                        parameter.ParameterName,
                        parameter.Value))
                    .ToArray()));
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed record CapturedCommand(
        string Text,
        IReadOnlyList<CapturedParameter> Parameters);

    private sealed record CapturedParameter(string Name, object? Value);

    private sealed record RankedSearchCapture(
        IReadOnlyList<CapturedCommand> ActivePage,
        IReadOnlyList<CapturedCommand> ContinuationPage);
}
