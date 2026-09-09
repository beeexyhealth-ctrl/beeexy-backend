using System.Text.Json.Nodes;
using Beeexy.Api.Operations;
using Beeexy.Application.Care;
using Beeexy.Application.Triage;
using Beeexy.Domain.Care;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase93")]
public sealed class AndreaSymptomDiaryImportTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private readonly string _databaseName = $"phase93_{Guid.NewGuid():N}";

    private string ConnectionString => ConnectionStringFor(_databaseName);

    [Fact]
    public async Task ControlledProductionCommand_ImportsFourGraphsAndRerunsIdempotently()
    {
        var databaseName = $"phase93_cli_{Guid.NewGuid():N}";
        var connectionString = ConnectionStringFor(databaseName);
        await CreateDatabaseAsync(databaseName);
        try
        {
            await using (var migrate = CreateDbContext(connectionString))
            {
                await migrate.Database.MigrateAsync();
                Assert.Empty(await migrate.SymptomDiaryPackageVersions.ToArrayAsync());
            }

            var firstOutput = new StringWriter();
            await Phase9SymptomContentCli.ExecuteAsync(
                Configuration(connectionString),
                Environments.Production,
                firstOutput);
            Assert.All(OutputLines(firstOutput), line => Assert.Contains("status=Imported", line));
            Assert.All(OutputLines(firstOutput), line =>
                Assert.Contains("review=Reviewed approval=Approved active=true", line));

            await using (var firstVerify = CreateDbContext(connectionString))
            {
                Assert.Equal(4, await firstVerify.SymptomDiaryPackageVersions.CountAsync());
                Assert.Equal(16, await firstVerify.SymptomDiaryQuestions.CountAsync());
                Assert.Equal(55, await firstVerify.SymptomDiaryQuestionOptions.CountAsync());
                Assert.Equal(35, await firstVerify.SymptomWarningSigns.CountAsync());
            }

            var persistedImportedAt = await PackageImportedTimesAsync(connectionString);
            var secondOutput = new StringWriter();
            await Phase9SymptomContentCli.ExecuteAsync(
                Configuration(connectionString),
                Environments.Production,
                secondOutput);
            Assert.All(OutputLines(secondOutput), line =>
                Assert.Contains("status=AlreadyImported", line));
            Assert.Equal(persistedImportedAt, await PackageImportedTimesAsync(connectionString));

            await using var secondVerify = CreateDbContext(connectionString);
            Assert.Equal(4, await secondVerify.SymptomDiaryPackageVersions.CountAsync());
            Assert.Equal(16, await secondVerify.SymptomDiaryQuestions.CountAsync());
            Assert.Equal(55, await secondVerify.SymptomDiaryQuestionOptions.CountAsync());
            Assert.Equal(35, await secondVerify.SymptomWarningSigns.CountAsync());
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task ControlledDevelopmentCommand_PrintsSafeTargetImportsExactReleaseAndRerunsIdempotently()
    {
        var databaseName = $"phase93_dev_cli_{Guid.NewGuid():N}";
        var connectionString = ConnectionStringFor(databaseName);
        await CreateDatabaseAsync(databaseName);
        try
        {
            await using (var migrate = CreateDbContext(connectionString))
            {
                await migrate.Database.MigrateAsync();
                Assert.Empty(await migrate.SymptomDiaryPackageVersions.ToArrayAsync());
            }

            var connectionTarget = new NpgsqlConnectionStringBuilder(connectionString);
            var firstOutput = new StringWriter();
            await Phase9SymptomContentCli.ExecuteDevelopmentAsync(
                Configuration(connectionString),
                Environments.Development,
                firstOutput);
            var firstLines = OutputLines(firstOutput);
            Assert.Equal(
                $"targetDatabaseHost={connectionTarget.Host} targetDatabase={databaseName}",
                firstLines[0]);
            Assert.False(string.IsNullOrEmpty(connectionTarget.Username));
            Assert.False(string.IsNullOrEmpty(connectionTarget.Password));
            Assert.DoesNotContain(connectionTarget.Username!, firstOutput.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(connectionTarget.Password!, firstOutput.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(5, firstLines.Length);
            Assert.All(firstLines[1..], line => Assert.Contains("status=Imported", line));

            var persistedImportedAt = await PackageImportedTimesAsync(connectionString);
            var secondOutput = new StringWriter();
            await Phase9SymptomContentCli.ExecuteDevelopmentAsync(
                Configuration(connectionString),
                Environments.Development,
                secondOutput);
            var secondLines = OutputLines(secondOutput);
            Assert.Equal(firstLines[0], secondLines[0]);
            Assert.Equal(5, secondLines.Length);
            Assert.All(secondLines[1..], line =>
                Assert.Contains("status=AlreadyImported", line));
            Assert.Equal(persistedImportedAt, await PackageImportedTimesAsync(connectionString));

            await using var verify = CreateDbContext(connectionString);
            var persisted = await verify.SymptomDiaryPackageVersions
                .AsNoTracking()
                .Include(value => value.Questions)
                .ThenInclude(value => value.Options)
                .Include(value => value.WarningSigns)
                .OrderBy(value => value.PackageCode)
                .ToArrayAsync();
            Assert.Equal(4, persisted.Length);
            foreach (var entity in persisted)
            {
                var content = await CreateProvider(verify).GetExactPackageAsync(entity.Id);
                Assert.NotNull(content);
                var expected = AndreaSymptomDiaryPackages.Create(content.Definition.Pathway);
                AssertDefinition(expected, content.Definition);
                Assert.Equal(expected.ExpectedContentHash, content.CanonicalContentHash);
            }
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task ExactLookup_RoundTripsEveryReleaseWithTextOrderHashAndProvenance()
    {
        var imported = await ImportAllAsync();

        foreach (var result in imported)
        {
            await using var read = CreateDbContext();
            var content = await CreateProvider(read).GetExactPackageAsync(result.PackageVersionId);
            Assert.NotNull(content);
            var expected = AndreaSymptomDiaryPackages.Create(content.Definition.Pathway);
            AssertDefinition(expected, content.Definition);
            Assert.Equal(expected.ExpectedContentHash, content.CanonicalContentHash);
            Assert.Equal(ClinicalContentSource.MedicalTeamProvided,
                content.Definition.ContentStatus.Source);
            Assert.Equal(AndreaSymptomDiaryPackages.SourceReference,
                content.Definition.SourceReference);
        }
    }

    [Fact]
    public async Task SameReleaseIdentityWithChangedContent_FailsAndRetainsOriginal()
    {
        var expected = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        SymptomDiaryPackageImportResult imported;
        await using (var first = CreateDbContext())
        {
            imported = await CreateImporter(first).ImportAsync(expected);
        }

        var changed = expected with
        {
            InformationalBody = "Test-only conflicting content",
            ExpectedContentHash = null
        };
        await using (var conflict = CreateDbContext())
        {
            await Assert.ThrowsAsync<SymptomDiaryPackageConflictException>(() =>
                CreateImporter(conflict).ImportAsync(changed));
        }

        await using var read = CreateDbContext();
        var original = await CreateProvider(read).GetExactPackageAsync(imported.PackageVersionId);
        Assert.NotNull(original);
        AssertDefinition(expected, original.Definition);
    }

    [Fact]
    public async Task ApprovalMakesExactlyTheFourSourcePathwaysActiveWithNoCrossPathwayFallback()
    {
        await ImportAllAsync();

        foreach (var pathway in new[]
                 {
                     ClinicalPathways.Headache,
                     ClinicalPathways.AbdominalPain,
                     ClinicalPathways.Fever,
                     ClinicalPathways.ChestPain
                 })
        {
            await using var read = CreateDbContext();
            var active = await CreateProvider(read).GetActivePackageAsync(pathway);
            Assert.NotNull(active);
            Assert.Equal(pathway, active.Definition.Pathway);
            Assert.Equal(AndreaSymptomDiaryPackages.VersionIdentifier,
                active.Definition.PackageVersion.Value);
            Assert.Equal(ClinicalContentStatus.MedicalTeamApproved,
                active.Definition.ContentStatus);
            Assert.Equal(AndreaSymptomDiaryPackages.ApprovalEffectiveAt,
                active.Definition.ApprovedAt);
            Assert.Equal(AndreaSymptomDiaryPackages.ApprovalEffectiveAt,
                active.Definition.ActivatedAt);
        }

        await using var unrelated = CreateDbContext();
        Assert.Null(await CreateProvider(unrelated).GetActivePackageAsync(
            ClinicalPathways.RespiratorySymptoms));
        Assert.Null(await CreateProvider(unrelated).GetActivePackageAsync(
            ClinicalPathways.OtherSymptoms));
    }

    [Fact]
    public async Task ConcurrentIdenticalImport_ConvergesWithoutDuplicateSourceChildren()
    {
        var package = TestIdentity(
            AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever));
        await using var first = CreateDbContext();
        await using var second = CreateDbContext();

        var results = await Task.WhenAll(
            CreateImporter(first).ImportAsync(package),
            CreateImporter(second).ImportAsync(package));

        Assert.Single(results, value => value.Outcome == SymptomDiaryPackageImportOutcome.Imported);
        Assert.Single(results,
            value => value.Outcome == SymptomDiaryPackageImportOutcome.AlreadyImported);
        await using var verify = CreateDbContext();
        var persisted = await verify.SymptomDiaryPackageVersions
            .Include(value => value.Questions)
            .ThenInclude(value => value.Options)
            .Include(value => value.WarningSigns)
            .SingleAsync(value => value.PackageCode == package.PackageCode);
        Assert.Equal(package.Questions.Count, persisted.Questions.Count);
        Assert.Equal(
            package.Questions.Sum(value => value.Options.Count),
            persisted.Questions.Sum(value => value.Options.Count));
        Assert.Equal(package.WarningSigns.Count, persisted.WarningSigns.Count);
    }

    [Fact]
    public async Task ConcurrentConflictingImport_ProducesOneWinnerAndOneSafeConflict()
    {
        var package = TestIdentity(
            AndreaSymptomDiaryPackages.Create(ClinicalPathways.AbdominalPain));
        var changed = package with { InformationalBody = "Test-only conflict B" };
        await using var first = CreateDbContext();
        await using var second = CreateDbContext();

        var outcomes = await Task.WhenAll(
            CaptureAsync(CreateImporter(first), package),
            CaptureAsync(CreateImporter(second), changed));

        Assert.Single(outcomes,
            value => value.Result?.Outcome == SymptomDiaryPackageImportOutcome.Imported);
        Assert.Single(outcomes,
            value => value.Error is SymptomDiaryPackageConflictException);
        await using var verify = CreateDbContext();
        Assert.Equal(1, await verify.SymptomDiaryPackageVersions.CountAsync(value =>
            value.PackageCode == package.PackageCode));
    }

    [Fact]
    public async Task InvalidSourceDerivedPackage_PersistsNoPartialGraph()
    {
        var package = TestIdentity(
            AndreaSymptomDiaryPackages.Create(ClinicalPathways.ChestPain));
        var duplicate = package.Questions[1] with { Code = package.Questions[0].Code };
        var invalid = package with
        {
            Questions = [package.Questions[0], duplicate, .. package.Questions.Skip(2)]
        };

        await using (var import = CreateDbContext())
        {
            await Assert.ThrowsAsync<SymptomDiaryPackageValidationException>(() =>
                CreateImporter(import).ImportAsync(invalid));
        }

        await using var verify = CreateDbContext();
        Assert.False(await verify.SymptomDiaryPackageVersions.AnyAsync(value =>
            value.PackageCode == package.PackageCode));
    }

    [Fact]
    public async Task Phase4DefinitionsAndRegistryRemainExactlyAsTheyWereBeforeImport()
    {
        var phase4Package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        await using (var seed = CreateDbContext())
        {
            await new ClinicalDefinitionImporter(
                seed,
                new ClinicalDefinitionPackageValidator(),
                NullLogger<ClinicalDefinitionImporter>.Instance)
                .ImportAsync(phase4Package);
        }

        await using (var import = CreateDbContext())
        {
            await CreateImporter(import).ImportAsync(
                AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache));
        }

        await using var verify = CreateDbContext();
        var questionnaire = await verify.QuestionnaireVersions.AsNoTracking()
            .SingleAsync(value => value.Id == phase4Package.Questionnaire.Id);
        var ruleSet = await verify.ClinicalRuleSetVersions.AsNoTracking()
            .SingleAsync(value => value.Id == phase4Package.RuleSet.Id);
        Assert.Equal(phase4Package.Questionnaire.ContentHash, questionnaire.ContentHash);
        Assert.Equal(phase4Package.RuleSet.ContentHash, ruleSet.ContentHash);
        Assert.Equal(
            [
                ClinicalPathways.Headache,
                ClinicalPathways.AbdominalPain,
                ClinicalPathways.ChestPain,
                ClinicalPathways.Fever,
                ClinicalPathways.OtherSymptoms
            ],
            ClinicalPathways.Supported);
    }

    [Fact]
    public async Task WarningSignsRemainStandaloneDisplayRowsWithoutAnswerMappings()
    {
        await ImportAllAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = 'care' AND table_name = 'symptom_warning_signs' " +
            "ORDER BY ordinal_position;";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(
            ["id", "package_version_id", "code", "display_text", "source_order"],
            columns);
    }

    private async Task<SymptomDiaryPackageImportResult[]> ImportAllAsync()
    {
        var results = new List<SymptomDiaryPackageImportResult>();
        foreach (var package in AndreaSymptomDiaryPackages.CreateAll())
        {
            await using var context = CreateDbContext();
            results.Add(await CreateImporter(context).ImportAsync(package));
        }

        return results.ToArray();
    }

    private static SymptomDiaryPackageDefinition TestIdentity(
        SymptomDiaryPackageDefinition package) => package with
        {
            PackageCode = SymptomDiaryCode.Create($"phase93-test-{Guid.NewGuid():N}"),
            ExpectedContentHash = null
        };

    private static async Task<ImportAttempt> CaptureAsync(
        ISymptomDiaryContentImporter importer,
        SymptomDiaryPackageDefinition package)
    {
        try
        {
            return new(await importer.ImportAsync(package), null);
        }
        catch (Exception exception)
        {
            return new(null, exception);
        }
    }

    private static void AssertDefinition(
        SymptomDiaryPackageDefinition expected,
        SymptomDiaryPackageDefinition actual)
    {
        Assert.Equal(expected.PackageCode, actual.PackageCode);
        Assert.Equal(expected.PackageVersion, actual.PackageVersion);
        Assert.Equal(expected.QuestionSetCode, actual.QuestionSetCode);
        Assert.Equal(expected.QuestionSetVersion, actual.QuestionSetVersion);
        Assert.Equal(expected.SymptomInformationCode, actual.SymptomInformationCode);
        Assert.Equal(expected.SymptomInformationVersion, actual.SymptomInformationVersion);
        Assert.Equal(expected.Pathway, actual.Pathway);
        Assert.Equal(expected.ContentStatus, actual.ContentStatus);
        Assert.Equal(expected.SourceReference, actual.SourceReference);
        Assert.Equal(expected.ImportedAt, actual.ImportedAt);
        Assert.Equal(expected.ApprovedAt, actual.ApprovedAt);
        Assert.Equal(expected.ActivatedAt, actual.ActivatedAt);
        Assert.Equal(expected.InformationalHeading, actual.InformationalHeading);
        Assert.Equal(expected.InformationalBody, actual.InformationalBody);
        Assert.Equal(expected.Questions.Count, actual.Questions.Count);
        for (var questionIndex = 0; questionIndex < expected.Questions.Count; questionIndex++)
        {
            var expectedQuestion = expected.Questions[questionIndex];
            var actualQuestion = actual.Questions[questionIndex];
            Assert.Equal(expectedQuestion.Code, actualQuestion.Code);
            Assert.Equal(expectedQuestion.PromptText, actualQuestion.PromptText);
            Assert.Equal(expectedQuestion.SourceOrder, actualQuestion.SourceOrder);
            Assert.Equal(expectedQuestion.IsRequired, actualQuestion.IsRequired);
            Assert.True(JsonNode.DeepEquals(
                JsonNode.Parse(expectedQuestion.AnswerSchemaJson),
                JsonNode.Parse(actualQuestion.AnswerSchemaJson)));
            Assert.Equal(expectedQuestion.Options, actualQuestion.Options);
        }

        Assert.Equal(expected.WarningSigns, actual.WarningSigns);
    }

    private SymptomDiaryContentImporter CreateImporter(BeeexyDbContext dbContext)
    {
        var serializer = new SymptomDiaryPackageCanonicalSerializer();
        return new(
            dbContext,
            new SymptomDiaryPackageValidator(),
            serializer,
            new SymptomDiaryPackageHashCalculator(serializer),
            NullLogger<SymptomDiaryContentImporter>.Instance);
    }

    private static SymptomDiaryContentProvider CreateProvider(BeeexyDbContext dbContext)
    {
        var serializer = new SymptomDiaryPackageCanonicalSerializer();
        return new(
            dbContext,
            new SymptomDiaryPackageValidator(),
            new SymptomDiaryPackageHashCalculator(serializer));
    }

    private BeeexyDbContext CreateDbContext() => CreateDbContext(ConnectionString);

    private static BeeexyDbContext CreateDbContext(string connectionString) => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BeeexyDatabase"] = connectionString
            })
            .Build();

    private static string[] OutputLines(StringWriter output) => output.ToString().Split(
        ["\r\n", "\n"],
        StringSplitOptions.RemoveEmptyEntries);

    private async Task<DateTimeOffset[]> PackageImportedTimesAsync(string connectionString)
    {
        await using var context = CreateDbContext(connectionString);
        return await context.SymptomDiaryPackageVersions.AsNoTracking()
            .OrderBy(value => value.PackageCode)
            .Select(value => value.ImportedAt)
            .ToArrayAsync();
    }

    private string ConnectionStringFor(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    private async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\";";
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync(_databaseName);
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => DropDatabaseAsync(_databaseName);

    private sealed record ImportAttempt(
        SymptomDiaryPackageImportResult? Result,
        Exception? Error);
}
