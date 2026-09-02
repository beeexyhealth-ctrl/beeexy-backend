using System.Net;
using System.Text.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class DirectoryPersistenceTests(PostgreSqlContainerFixture postgres)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 28, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NormalizedDirectoryGraph_PersistsWithExactCredentialVocabulary()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(
                graph.Clinic,
                graph.Location,
                graph.Doctor,
                graph.Affiliation,
                graph.Specialty,
                graph.DoctorSpecialty,
                graph.Language,
                graph.DoctorLanguage,
                graph.InsurancePlan,
                graph.Participation,
                graph.MatchRuleVersion);
            dbContext.DoctorCredentials.AddRange(graph.Credentials);
            await dbContext.SaveChangesAsync();
        }

        await using var verify = CreateDbContext();
        var location = await verify.ClinicLocations.AsNoTracking()
            .SingleAsync(value => value.Id == graph.Location.Id);
        var statuses = await verify.DoctorCredentials.AsNoTracking()
            .Where(value => value.DoctorId == graph.Doctor.Id)
            .OrderBy(value => value.Status)
            .Select(value => value.Status)
            .ToListAsync();

        Assert.Equal(graph.Clinic.Id, location.ClinicId);
        Assert.Equal("America/Lima", location.TimeZone.Value);
        Assert.Equal(Enum.GetValues<DoctorCredentialStatus>().Order(), statuses.Order());
        Assert.Equal(graph.Doctor.Id, graph.DoctorSpecialty.DoctorId);
        Assert.Equal(graph.Doctor.Id, graph.DoctorLanguage.DoctorId);
        Assert.Equal(graph.Doctor.Id, graph.Participation.DoctorId);
        Assert.Equal(
            graph.MatchRuleVersion.Id,
            (await verify.DoctorMatchRuleVersions.AsNoTracking().SingleAsync(
                value => value.Id == graph.MatchRuleVersion.Id)).Id);
    }

    [Fact]
    public async Task ClinicAndDoctorCodes_AreUniquelyEnforced()
    {
        await EnsureMigratedAsync();
        var clinicCode = DirectoryCode.Create($"clinic-{Guid.NewGuid():N}");
        var doctorCode = DirectoryCode.Create($"doctor-{Guid.NewGuid():N}");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(
                Clinic.Create(clinicCode, DirectoryName.Create("Clinic record one"), false, CreatedAt),
                Doctor.Create(doctorCode, DirectoryName.Create("Doctor record one"), false, CreatedAt));
            await dbContext.SaveChangesAsync();
        }

        await AssertUniqueViolationAsync(
            Clinic.Create(clinicCode, DirectoryName.Create("Clinic record two"), false, CreatedAt),
            "ux_clinics_code");
        await AssertUniqueViolationAsync(
            Doctor.Create(doctorCode, DirectoryName.Create("Doctor record two"), false, CreatedAt),
            "ux_doctors_code");
    }

    [Fact]
    public async Task Affiliation_RejectsLocationFromAnotherClinicAndDuplicateStructures()
    {
        await EnsureMigratedAsync();
        var doctor = CreateDoctor();
        var firstClinic = CreateClinic();
        var secondClinic = CreateClinic();
        var secondLocation = CreateLocation(secondClinic.Id);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(doctor, firstClinic, secondClinic, secondLocation);
            await dbContext.SaveChangesAsync();
        }

        await using (var mismatch = CreateDbContext())
        {
            mismatch.DoctorAffiliations.Add(DoctorAffiliation.Create(
                doctor.Id,
                firstClinic.Id,
                secondLocation.Id,
                false,
                CreatedAt));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                mismatch.SaveChangesAsync());
            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
            Assert.Equal(
                "fk_doctor_affiliations_clinic_locations",
                postgresException.ConstraintName);
        }

        var first = DoctorAffiliation.Create(
            doctor.Id,
            firstClinic.Id,
            null,
            false,
            CreatedAt);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.DoctorAffiliations.Add(first);
            await dbContext.SaveChangesAsync();
        }

        await AssertUniqueViolationAsync(
            DoctorAffiliation.Create(
                doctor.Id,
                firstClinic.Id,
                null,
                true,
                CreatedAt),
            "ux_doctor_affiliations_clinic_only");
    }

    [Fact]
    public async Task NormalizedRelationships_AreUniqueAndRestrictPrincipalDeletion()
    {
        await EnsureMigratedAsync();
        var doctor = CreateDoctor();
        var specialty = Specialty.Create(
            DirectoryCode.Create($"specialty-{Guid.NewGuid():N}"),
            DirectoryName.Create("Specialty record"),
            CreatedAt);
        var relationship = DoctorSpecialty.Create(doctor.Id, specialty.Id, CreatedAt);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(doctor, specialty, relationship);
            await dbContext.SaveChangesAsync();
        }

        await AssertUniqueViolationAsync(
            DoctorSpecialty.Create(doctor.Id, specialty.Id, CreatedAt),
            "ux_doctor_specialties_doctor_specialty");

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM directory.doctors WHERE id = @id;";
        command.Parameters.AddWithValue("id", doctor.Id.Value);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_doctor_specialties_doctors_doctor_id", exception.ConstraintName);
    }

    [Fact]
    public async Task Migration_CreatesExpectedDirectoryTablesConstraintsAndIndexes()
    {
        await EnsureMigratedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        var tables = await ReadStringsAsync(
            connection,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'directory' ORDER BY table_name;");
        Assert.Equal(
            [
                "clinic_locations",
                "clinics",
                "demo_directory_imports",
                "doctor_affiliations",
                "doctor_credentials",
                "doctor_insurance_participations",
                "doctor_languages",
                "doctor_match_rule_configurations",
                "doctor_match_rule_versions",
                "doctor_specialties",
                "doctors",
                "insurance_plans",
                "languages",
                "specialties"
            ],
            tables);

        var uuidColumns = await ReadStringsAsync(
            connection,
            "SELECT table_name || '.' || column_name FROM information_schema.columns " +
            "WHERE table_schema = 'directory' AND udt_name = 'uuid' ORDER BY 1;");
        Assert.Contains("clinics.id", uuidColumns);
        Assert.Contains("doctors.id", uuidColumns);
        Assert.Contains("doctor_affiliations.clinic_location_id", uuidColumns);
        Assert.Contains("doctor_insurance_participations.insurance_plan_id", uuidColumns);
        Assert.Contains("doctor_match_rule_configurations.rule_version_id", uuidColumns);

        var indexes = await ReadStringsAsync(
            connection,
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'directory' " +
            "AND indexname IN (" +
            "'ux_clinics_code','ux_doctors_code','ix_clinics_published'," +
            "'ix_doctors_published','ix_clinic_locations_area_published'," +
            "'ix_doctor_credentials_doctor_status'," +
            "'ix_doctor_specialties_specialty_id'," +
            "'ix_doctor_languages_language_id','ix_doctor_insurance_plan_id'," +
            "'ix_doctor_match_rule_configurations_package_code'," +
            "'ux_doctor_match_rule_versions_version') ORDER BY indexname;");
        Assert.Equal(11, indexes.Count);

        await using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText =
            "SELECT rc.delete_rule FROM information_schema.table_constraints tc " +
            "JOIN information_schema.referential_constraints rc " +
            "ON rc.constraint_schema = tc.constraint_schema " +
            "AND rc.constraint_name = tc.constraint_name " +
            "WHERE tc.constraint_type = 'FOREIGN KEY' " +
            "AND tc.table_schema = 'directory';";
        var deleteRules = new List<string>();
        await using var reader = await foreignKeyCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            deleteRules.Add(reader.GetString(0));
        }

        Assert.Equal(12, deleteRules.Count);
        Assert.All(deleteRules, rule => Assert.Equal("RESTRICT", rule));
    }

    [Fact]
    public async Task PostgreSql_RequiresLocationTimezoneAndApprovedCredentialStatus()
    {
        await EnsureMigratedAsync();
        var clinic = CreateClinic();
        var doctor = CreateDoctor();
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(clinic, doctor);
            await dbContext.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var location = connection.CreateCommand())
        {
            location.CommandText =
                "INSERT INTO directory.clinic_locations " +
                "(id, clinic_id, name, locality, administrative_area, country, " +
                "is_published, created_at) VALUES " +
                "(@id, @clinic, 'Location', 'Locality', 'Area', 'Country', false, @created);";
            location.Parameters.AddWithValue("id", Guid.NewGuid());
            location.Parameters.AddWithValue("clinic", clinic.Id.Value);
            location.Parameters.AddWithValue("created", CreatedAt);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                location.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
            Assert.Equal("timezone", exception.ColumnName);
        }

        await using (var credential = connection.CreateCommand())
        {
            credential.CommandText =
                "INSERT INTO directory.doctor_credentials " +
                "(id, doctor_id, name, status, created_at) VALUES " +
                "(@id, @doctor, 'Claim', 'externally_verified', @created);";
            credential.Parameters.AddWithValue("id", Guid.NewGuid());
            credential.Parameters.AddWithValue("doctor", doctor.Id.Value);
            credential.Parameters.AddWithValue("created", CreatedAt);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                credential.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_doctor_credentials_status", exception.ConstraintName);
        }
    }

    [Fact]
    public async Task OpenApi_AddsOnlyThePublicClinicDirectoryRoutes()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(46, paths.EnumerateObject().Count());
        Assert.Equal(2, paths.EnumerateObject().Count(
            path => path.Name.StartsWith("/api/v1/clinics", StringComparison.Ordinal)));
        Assert.Equal(3, paths.EnumerateObject().Count(
            path => path.Name.StartsWith("/api/v1/doctors", StringComparison.Ordinal)));
    }

    private async Task AssertUniqueViolationAsync(object entity, string constraintName)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Add(entity);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }

    private static async Task<List<string>> ReadStringsAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private DirectoryGraph CreateGraph()
    {
        var clinic = CreateClinic();
        var location = CreateLocation(clinic.Id);
        var doctor = CreateDoctor();
        var specialty = Specialty.Create(
            DirectoryCode.Create($"specialty-{Guid.NewGuid():N}"),
            DirectoryName.Create("Specialty record"),
            CreatedAt);
        var language = Language.Create(
            DirectoryCode.Create($"language-{Guid.NewGuid():N}"),
            DirectoryName.Create("Language record"),
            CreatedAt);
        var insurancePlan = InsurancePlan.Create(
            DirectoryCode.Create($"plan-{Guid.NewGuid():N}"),
            DirectoryName.Create("Insurance plan record"),
            CreatedAt);
        return new DirectoryGraph(
            clinic,
            location,
            doctor,
            DoctorAffiliation.Create(
                doctor.Id,
                clinic.Id,
                location.Id,
                true,
                CreatedAt),
            Enum.GetValues<DoctorCredentialStatus>()
                .Select(status => DoctorCredential.Create(
                    doctor.Id,
                    DirectoryName.Create($"{status} demo claim"),
                    status,
                    CreatedAt))
                .ToArray(),
            specialty,
            DoctorSpecialty.Create(doctor.Id, specialty.Id, CreatedAt),
            language,
            DoctorLanguage.Create(doctor.Id, language.Id, CreatedAt),
            insurancePlan,
            DoctorInsuranceParticipation.Create(doctor.Id, insurancePlan.Id, CreatedAt),
            DoctorMatchRuleVersion.Create(
                DirectoryCode.Create($"demo-version-{Guid.NewGuid():N}"),
                CreatedAt));
    }

    private static Clinic CreateClinic()
    {
        return Clinic.Create(
            DirectoryCode.Create($"clinic-{Guid.NewGuid():N}"),
            DirectoryName.Create("Synthetic clinic test record"),
            false,
            CreatedAt);
    }

    private static Doctor CreateDoctor()
    {
        return Doctor.Create(
            DirectoryCode.Create($"doctor-{Guid.NewGuid():N}"),
            DirectoryName.Create("Synthetic doctor test record"),
            false,
            CreatedAt);
    }

    private static ClinicLocation CreateLocation(EntityId clinicId)
    {
        return ClinicLocation.Create(
            clinicId,
            DirectoryName.Create("Synthetic location test record"),
            "Lima",
            "Lima",
            "Peru",
            IanaTimeZone.Create("America/Lima"),
            false,
            CreatedAt);
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private sealed record DirectoryGraph(
        Clinic Clinic,
        ClinicLocation Location,
        Doctor Doctor,
        DoctorAffiliation Affiliation,
        DoctorCredential[] Credentials,
        Specialty Specialty,
        DoctorSpecialty DoctorSpecialty,
        Language Language,
        DoctorLanguage DoctorLanguage,
        InsurancePlan InsurancePlan,
        DoctorInsuranceParticipation Participation,
        DoctorMatchRuleVersion MatchRuleVersion);
}
