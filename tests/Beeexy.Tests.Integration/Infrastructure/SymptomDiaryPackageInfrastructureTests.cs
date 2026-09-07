using System.Text.Json.Nodes;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase92")]
public sealed class SymptomDiaryPackageInfrastructureTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    [Fact]
    public async Task ValidPackage_ImportsAtomicallyAndRoundTripsCompleteExactGraph()
    {
        var package = CreatePackage();
        SymptomDiaryPackageImportResult result;
        await using (var import = CreateDbContext())
        {
            result = await CreateImporter(import).ImportAsync(package);
        }

        Assert.Equal(SymptomDiaryPackageImportOutcome.Imported, result.Outcome);
        Assert.Matches("^[0-9a-f]{64}$", result.CanonicalContentHash.Value);

        await using var read = CreateDbContext();
        var content = await CreateProvider(read).GetExactPackageAsync(result.PackageVersionId);
        Assert.NotNull(content);
        Assert.Equal(result.PackageVersionId, content.PackageVersionId);
        Assert.Equal(result.CanonicalContentHash, content.CanonicalContentHash);
        Assert.Equal(package.PackageCode, content.Definition.PackageCode);
        Assert.Equal(package.PackageVersion, content.Definition.PackageVersion);
        Assert.Equal(package.InformationalHeading, content.Definition.InformationalHeading);
        Assert.Equal(package.InformationalBody, content.Definition.InformationalBody);
        Assert.Equal([1, 2], content.Definition.Questions.Select(value => value.SourceOrder));
        Assert.Equal(
            ["Synthetic prompt A", "Synthetic prompt B"],
            content.Definition.Questions.Select(value => value.PromptText));
        Assert.Equal(
            ["alpha", "beta"],
            content.Definition.Questions[0].Options.Select(value => value.Value));
        Assert.Equal(
            ["Synthetic Alpha", "Synthetic Beta"],
            content.Definition.Questions[0].Options.Select(value => value.DisplayText));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(package.Questions[0].AnswerSchemaJson),
            JsonNode.Parse(content.Definition.Questions[0].AnswerSchemaJson)));
        Assert.Equal([1, 2], content.Definition.WarningSigns.Select(value => value.SourceOrder));
        Assert.Equal(
            ["Synthetic information A", "Synthetic information B"],
            content.Definition.WarningSigns.Select(value => value.DisplayText));
    }

    [Fact]
    public async Task IdenticalRetry_IsIdempotentWithoutRewritingOrDuplicatingChildren()
    {
        var package = CreatePackage();
        SymptomDiaryPackageImportResult first;
        await using (var initial = CreateDbContext())
        {
            first = await CreateImporter(initial).ImportAsync(package);
        }

        await using (var retry = CreateDbContext())
        {
            var second = await CreateImporter(retry).ImportAsync(
                package with { ImportedAt = package.ImportedAt.AddMinutes(5) });
            Assert.Equal(SymptomDiaryPackageImportOutcome.AlreadyImported, second.Outcome);
            Assert.Equal(first.PackageVersionId, second.PackageVersionId);
            Assert.Equal(first.CanonicalContentHash, second.CanonicalContentHash);
        }

        await using var verify = CreateDbContext();
        var persisted = await verify.SymptomDiaryPackageVersions.AsNoTracking()
            .Include(value => value.Questions)
            .ThenInclude(value => value.Options)
            .Include(value => value.WarningSigns)
            .SingleAsync(value => value.Id == first.PackageVersionId);
        Assert.Equal(package.ImportedAt, persisted.ImportedAt);
        Assert.Equal(2, persisted.Questions.Count);
        Assert.Equal(2, persisted.Questions.Sum(value => value.Options.Count));
        Assert.Equal(2, persisted.WarningSigns.Count);
    }

    [Fact]
    public async Task SameIdentityWithChangedContent_FailsClosedAndRetainsOriginalGraph()
    {
        var package = CreatePackage();
        SymptomDiaryPackageImportResult first;
        await using (var initial = CreateDbContext())
        {
            first = await CreateImporter(initial).ImportAsync(package);
        }

        var changedFirst = package.Questions[0] with
        {
            PromptText = "Conflicting synthetic prompt"
        };
        await using (var conflict = CreateDbContext())
        {
            await Assert.ThrowsAsync<SymptomDiaryPackageConflictException>(() =>
                CreateImporter(conflict).ImportAsync(
                    package with { Questions = [changedFirst, package.Questions[1]] }));
        }

        await using var verify = CreateDbContext();
        var content = await CreateProvider(verify).GetExactPackageAsync(first.PackageVersionId);
        Assert.NotNull(content);
        Assert.Equal("Synthetic prompt A", content.Definition.Questions[0].PromptText);
        Assert.Equal(first.CanonicalContentHash, content.CanonicalContentHash);
        Assert.Equal(1, await verify.SymptomDiaryPackageVersions.CountAsync(value =>
            value.PackageCode == package.PackageCode &&
            value.PackageVersion == package.PackageVersion));
    }

    [Fact]
    public async Task InvalidPackage_PersistsZeroRows()
    {
        var package = CreatePackage();
        var invalidQuestion = package.Questions[1] with
        {
            Code = package.Questions[0].Code
        };

        await using (var import = CreateDbContext())
        {
            await Assert.ThrowsAsync<SymptomDiaryPackageValidationException>(() =>
                CreateImporter(import).ImportAsync(
                    package with { Questions = [package.Questions[0], invalidQuestion] }));
        }

        var hashMismatch = CreatePackage() with
        {
            ExpectedContentHash = SymptomDiarySha256.FromHash(new string('f', 64))
        };
        await using (var import = CreateDbContext())
        {
            await Assert.ThrowsAsync<SymptomDiaryPackageValidationException>(() =>
                CreateImporter(import).ImportAsync(hashMismatch));
        }

        await using var verify = CreateDbContext();
        Assert.False(await verify.SymptomDiaryPackageVersions.AnyAsync(value =>
            value.PackageCode == package.PackageCode));
        Assert.False(await verify.SymptomDiaryPackageVersions.AnyAsync(value =>
            value.PackageCode == hashMismatch.PackageCode));
    }

    [Fact]
    public async Task DatabaseFailure_RollsBackParentAndEveryChild()
    {
        var package = CreatePackage();
        var triggerSuffix = Guid.NewGuid().ToString("N");
        var function = $"care.phase92_reject_question_{triggerSuffix}";
        var trigger = $"phase92_reject_question_{triggerSuffix}";
        await ExecuteSqlAsync($$"""
            CREATE FUNCTION {{function}}() RETURNS trigger
            LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'synthetic phase 9.2 transactional failure';
            END;
            $body$;
            CREATE TRIGGER {{trigger}}
            BEFORE INSERT ON care.symptom_diary_questions
            FOR EACH ROW EXECUTE FUNCTION {{function}}();
            """);

        try
        {
            await using var import = CreateDbContext();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                CreateImporter(import).ImportAsync(package));
        }
        finally
        {
            await ExecuteSqlAsync($$"""
                DROP TRIGGER IF EXISTS {{trigger}} ON care.symptom_diary_questions;
                DROP FUNCTION IF EXISTS {{function}}();
                """);
        }

        await using var verify = CreateDbContext();
        Assert.False(await verify.SymptomDiaryPackageVersions.AnyAsync(value =>
            value.PackageCode == package.PackageCode));
    }

    [Fact]
    public async Task ExactHistoricalLookup_NeverSubstitutesNewerActivePackage()
    {
        var older = CreatePackage("v-z-older", ClinicalPathways.RespiratorySymptoms) with
        {
            ActivatedAt = ImportedAt.AddDays(1),
            InformationalHeading = "Synthetic historical heading"
        };
        var newer = CreatePackage("v-a-newer", ClinicalPathways.RespiratorySymptoms) with
        {
            ActivatedAt = ImportedAt.AddDays(2),
            InformationalHeading = "Synthetic active heading"
        };
        SymptomDiaryPackageImportResult olderResult;
        SymptomDiaryPackageImportResult newerResult;
        await using (var import = CreateDbContext())
        {
            olderResult = await CreateImporter(import).ImportAsync(older);
        }

        await using (var import = CreateDbContext())
        {
            newerResult = await CreateImporter(import).ImportAsync(newer);
        }

        await using var read = CreateDbContext();
        var provider = CreateProvider(read);
        var active = await provider.GetActivePackageAsync(ClinicalPathways.RespiratorySymptoms);
        var exactOlder = await provider.GetExactPackageAsync(olderResult.PackageVersionId);

        Assert.Equal(newerResult.PackageVersionId, active?.PackageVersionId);
        Assert.Equal("Synthetic active heading", active?.Definition.InformationalHeading);
        Assert.Equal(olderResult.PackageVersionId, exactOlder?.PackageVersionId);
        Assert.Equal("Synthetic historical heading", exactOlder?.Definition.InformationalHeading);
    }

    [Fact]
    public async Task ActiveLookup_ExcludesIneligibleContentAndNeverFallsBackAcrossPathways()
    {
        var provisional = CreatePackage(pathway: ClinicalPathways.BackPain) with
        {
            ContentStatus = ClinicalContentStatus.ProvisionalReferencePlatformDerived,
            ApprovedAt = null,
            ActivatedAt = null
        };
        var nonClinical = CreatePackage("v2", ClinicalPathways.BackPain) with
        {
            ContentStatus = ClinicalContentStatus.NonClinicalDemo,
            ApprovedAt = null,
            ActivatedAt = null
        };
        var otherPathwayActive = CreatePackage(
            "v3",
            ClinicalPathways.RespiratorySymptoms) with
        {
            ActivatedAt = ImportedAt.AddDays(3)
        };
        await using (var first = CreateDbContext())
        {
            await CreateImporter(first).ImportAsync(provisional);
        }

        await using (var second = CreateDbContext())
        {
            await CreateImporter(second).ImportAsync(nonClinical);
        }

        await using (var third = CreateDbContext())
        {
            await CreateImporter(third).ImportAsync(otherPathwayActive);
        }

        await using var read = CreateDbContext();
        var provider = CreateProvider(read);
        Assert.Null(await provider.GetActivePackageAsync(ClinicalPathways.BackPain));
        Assert.NotNull(await provider.GetExactPackageAsync((await FindAsync(provisional)).Id));
        Assert.NotNull(await provider.GetExactPackageAsync((await FindAsync(nonClinical)).Id));
    }

    [Fact]
    public async Task CorruptStoredHash_FailsClosedOnExactAndActiveLookup()
    {
        var package = CreatePackage(pathway: ClinicalPathways.OtherSymptoms) with
        {
            ActivatedAt = ImportedAt.AddDays(4)
        };
        var corrupt = SymptomDiaryPackageMapper.ToEntity(
            package,
            SymptomDiarySha256.FromHash(new string('f', 64)));
        await using (var seed = CreateDbContext())
        {
            seed.SymptomDiaryPackageVersions.Add(corrupt);
            await seed.SaveChangesAsync();
        }

        await using var read = CreateDbContext();
        var provider = CreateProvider(read);
        await Assert.ThrowsAsync<SymptomDiaryPackageIntegrityException>(() =>
            provider.GetExactPackageAsync(corrupt.Id));
        await Assert.ThrowsAsync<SymptomDiaryPackageIntegrityException>(() =>
            provider.GetActivePackageAsync(ClinicalPathways.OtherSymptoms));
    }

    [Fact]
    public async Task ConcurrentIdenticalImports_ConvergeWithoutDuplicates()
    {
        var package = CreatePackage();
        await using var first = CreateDbContext();
        await using var second = CreateDbContext();

        var results = await Task.WhenAll(
            CreateImporter(first).ImportAsync(package),
            CreateImporter(second).ImportAsync(package));

        Assert.Single(results, value =>
            value.Outcome == SymptomDiaryPackageImportOutcome.Imported);
        Assert.Single(results, value =>
            value.Outcome == SymptomDiaryPackageImportOutcome.AlreadyImported);
        Assert.Equal(results[0].PackageVersionId, results[1].PackageVersionId);
        await using var verify = CreateDbContext();
        var persisted = await verify.SymptomDiaryPackageVersions.AsNoTracking()
            .Include(value => value.Questions)
            .ThenInclude(value => value.Options)
            .Include(value => value.WarningSigns)
            .SingleAsync(value => value.PackageCode == package.PackageCode);
        Assert.Equal(2, persisted.Questions.Count);
        Assert.Equal(2, persisted.Questions.Sum(value => value.Options.Count));
        Assert.Equal(2, persisted.WarningSigns.Count);
    }

    [Fact]
    public async Task ConcurrentConflictingImports_ProduceOneWinnerAndOneSafeConflict()
    {
        var package = CreatePackage();
        var changed = package with
        {
            InformationalBody = "Conflicting synthetic body"
        };
        await using var first = CreateDbContext();
        await using var second = CreateDbContext();

        var outcomes = await Task.WhenAll(
            CaptureAsync(CreateImporter(first), package),
            CaptureAsync(CreateImporter(second), changed));

        Assert.Single(outcomes, value =>
            value.Result?.Outcome == SymptomDiaryPackageImportOutcome.Imported);
        Assert.Single(outcomes, value => value.Error is SymptomDiaryPackageConflictException);
        await using var verify = CreateDbContext();
        Assert.Equal(1, await verify.SymptomDiaryPackageVersions.CountAsync(value =>
            value.PackageCode == package.PackageCode));
    }

    [Fact]
    public async Task DatabaseContainsNoAutomaticallySeededProductionDiaryPackage()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM care.symptom_diary_package_versions " +
            "WHERE lower(coalesce(source_reference, '')) LIKE '%symptoms.md%' " +
            "OR (package_code NOT LIKE 'phase92-test-package-%' " +
            "AND package_code NOT LIKE 'test-diary-%');";

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private async Task<SymptomDiaryPackageVersion> FindAsync(
        SymptomDiaryPackageDefinition package)
    {
        await using var lookup = CreateDbContext();
        return await lookup.SymptomDiaryPackageVersions.AsNoTracking().SingleAsync(value =>
            value.PackageCode == package.PackageCode &&
            value.PackageVersion == package.PackageVersion);
    }

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

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static SymptomDiaryPackageDefinition CreatePackage(
        string version = "v1",
        ClinicalPathwayCode? pathway = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new(
            SymptomDiaryCode.Create($"phase92-test-package-{suffix}"),
            DefinitionVersion.Create(version),
            SymptomDiaryCode.Create($"phase92-test-questions-{suffix}"),
            DefinitionVersion.Create("questions-v1"),
            SymptomDiaryCode.Create($"phase92-test-information-{suffix}"),
            DefinitionVersion.Create("information-v1"),
            pathway ?? ClinicalPathways.BackPain,
            ClinicalContentStatus.MedicalTeamApproved,
            $"test/phase-9-2/{suffix}",
            ImportedAt,
            ImportedAt.AddHours(1),
            null,
            "Synthetic informational heading — niño 👩🏽‍⚕️",
            "Synthetic informational body\nSecond synthetic line: café, 中文",
            [
                new(
                    SymptomDiaryCode.Create("synthetic-question-a"),
                    "Synthetic prompt A",
                    1,
                    "{\"zeta\":{\"beta\":2,\"alpha\":1},\"type\":\"string\"}",
                    true,
                    [
                        new(
                            SymptomDiaryCode.Create("synthetic-option-a"),
                            "alpha",
                            "Synthetic Alpha",
                            1),
                        new(
                            SymptomDiaryCode.Create("synthetic-option-b"),
                            "beta",
                            "Synthetic Beta",
                            2)
                    ]),
                new(
                    SymptomDiaryCode.Create("synthetic-question-b"),
                    "Synthetic prompt B",
                    2,
                    "{\"type\":\"boolean\"}",
                    false,
                    [])
            ],
            [
                new(
                    SymptomDiaryCode.Create("synthetic-information-a"),
                    "Synthetic information A",
                    1),
                new(
                    SymptomDiaryCode.Create("synthetic-information-b"),
                    "Synthetic information B",
                    2)
            ]);
    }

    private static readonly DateTimeOffset ImportedAt =
        new(2090, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed record ImportAttempt(
        SymptomDiaryPackageImportResult? Result,
        Exception? Error);
}
