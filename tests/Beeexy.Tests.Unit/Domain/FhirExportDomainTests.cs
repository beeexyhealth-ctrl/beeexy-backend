using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Triage;

namespace Beeexy.Tests.Unit.Domain;

public sealed class FhirExportDomainTests
{
    [Fact]
    public void PendingExport_RetainsSourceIdentityExactVersionsAndIdempotency()
    {
        var source = CreateHistoryEvent();
        var idempotencyKey = EntityId.New();
        var versions = FhirExportVersionMetadata.Create(
            "future-release-defined-by-mapping",
            "beeexy-map-2026-08-24",
            "https://profiles.example.test/beeexy-export",
            "2026.08");

        var export = FhirExport.CreatePending(
            source,
            versions,
            idempotencyKey,
            Utc(17));

        Assert.NotEqual(Guid.Empty, export.Id.Value);
        Assert.Equal(source.PatientProfileId, export.PatientProfileId);
        Assert.Equal(source.Id, export.SourceClinicalHistoryEventId);
        Assert.Equal(versions, export.Versions);
        Assert.Equal(idempotencyKey, export.IdempotencyKey);
        Assert.Equal(FhirExportStatus.Pending, export.Status);
        Assert.Null(export.Artifact);
        Assert.Null(export.GeneratedAt);
        Assert.Null(export.ValidationCompletedAt);
        Assert.Equal(Utc(17), export.CreatedAt);
        Assert.Equal(export.CreatedAt, export.UpdatedAt);
    }

    [Fact]
    public void GeneratedExport_RetainsImmutableChecksumAndPrivateStorageMetadata()
    {
        var export = CreatePendingExport();
        var artifact = FhirArtifactMetadata.Create(
            "SHA-256",
            new string('a', 64),
            "s3://private-beeexy/fhir/export.json");

        export.MarkGenerated(artifact, Utc(18));

        Assert.Equal(FhirExportStatus.Generated, export.Status);
        Assert.Equal(artifact, export.Artifact);
        Assert.Equal(Utc(18), export.GeneratedAt);
        Assert.Equal(Utc(18), export.UpdatedAt);
        Assert.Throws<InvalidOperationException>(() => export.MarkGenerated(
            FhirArtifactMetadata.Create(
                "SHA-256",
                new string('b', 64),
                "s3://private-beeexy/fhir/replacement.json"),
            Utc(19)));
        Assert.Equal(artifact, export.Artifact);
    }

    [Fact]
    public void PassingValidation_ProducesValidatedExportBoundToArtifactChecksum()
    {
        var export = CreateGeneratedExport();

        var result = export.RecordValidation(
            FhirValidationOutcome.Passed,
            FhirValidatorMetadata.Create("validator-to-be-selected", "test-version"),
            errorCount: 0,
            warningCount: 2,
            validationCompletedAt: Utc(19));

        Assert.True(result.IsValid);
        Assert.Equal(export.Id, result.FhirExportId);
        Assert.Equal(export.ChecksumAlgorithm, result.ArtifactChecksumAlgorithm);
        Assert.Equal(export.Checksum, result.ArtifactChecksum);
        Assert.Equal(FhirExportStatus.Validated, export.Status);
        Assert.Equal(FhirValidationOutcome.Passed, export.ValidationOutcome);
        Assert.Equal(Utc(19), export.ValidationCompletedAt);
        Assert.Equal(Utc(19), export.ValidatedAt);
    }

    [Fact]
    public void FailedArtifact_CanNeverTransitionToValidated()
    {
        var export = CreateGeneratedExport();

        var result = export.RecordValidation(
            FhirValidationOutcome.Failed,
            FhirValidatorMetadata.Create("validator-to-be-selected", "test-version"),
            errorCount: 3,
            warningCount: 1,
            validationCompletedAt: Utc(19));

        Assert.False(result.IsValid);
        Assert.Equal(FhirExportStatus.ValidationFailed, export.Status);
        Assert.Equal(FhirValidationOutcome.Failed, export.ValidationOutcome);
        Assert.Null(export.ValidatedAt);
        Assert.Throws<InvalidOperationException>(() => export.RecordValidation(
            FhirValidationOutcome.Passed,
            FhirValidatorMetadata.Create("validator-to-be-selected", "test-version"),
            errorCount: 0,
            warningCount: 0,
            validationCompletedAt: Utc(20)));
        Assert.Equal(FhirExportStatus.ValidationFailed, export.Status);
    }

    [Fact]
    public void ValidationOutcomeAndErrorCounts_MustAgree()
    {
        var passingExport = CreateGeneratedExport();
        var failingExport = CreateGeneratedExport();
        var validator = FhirValidatorMetadata.Create("validator-to-be-selected", "test-version");

        Assert.Throws<ArgumentException>(() => passingExport.RecordValidation(
            FhirValidationOutcome.Passed,
            validator,
            errorCount: 1,
            warningCount: 0,
            validationCompletedAt: Utc(19)));
        Assert.Throws<ArgumentException>(() => failingExport.RecordValidation(
            FhirValidationOutcome.Failed,
            validator,
            errorCount: 0,
            warningCount: 0,
            validationCompletedAt: Utc(19)));
        Assert.Equal(FhirExportStatus.Generated, passingExport.Status);
        Assert.Equal(FhirExportStatus.Generated, failingExport.Status);
    }

    [Fact]
    public void ValidationResult_FromAnotherArtifactCannotBeApplied()
    {
        var first = CreateGeneratedExport(new string('a', 64));
        var second = CreateGeneratedExport(new string('b', 64));
        var result = FhirValidationResult.Create(
            first,
            FhirValidationOutcome.Passed,
            FhirValidatorMetadata.Create("validator-to-be-selected", "test-version"),
            errorCount: 0,
            warningCount: 0,
            validatedAt: Utc(19));

        Assert.Throws<ArgumentException>(() => second.ApplyValidationResult(result));
        Assert.Equal(FhirExportStatus.Generated, second.Status);
    }

    [Fact]
    public void VersionAndArtifactMetadata_RejectIncompleteOrUnsafeRepresentations()
    {
        Assert.Throws<ArgumentException>(() => FhirExportVersionMetadata.Create("", "map"));
        Assert.Throws<ArgumentException>(() => FhirExportVersionMetadata.Create(
            "release",
            "map",
            profileCanonical: "https://profiles.example.test/only-canonical"));
        Assert.Throws<ArgumentException>(() => FhirArtifactMetadata.Create(
            "SHA-256",
            "checksum",
            "relative/path.json"));
        Assert.Throws<ArgumentException>(() => FhirArtifactMetadata.Create(
            "SHA-256",
            "checksum",
            "https://secret:credential@storage.example.test/export.json"));
    }

    [Fact]
    public void DomainModel_HasNoFhirSdkResourcesAndExposesNoPublicSetters()
    {
        var domainAssembly = typeof(FhirExport).Assembly;
        var forbiddenResourceNames = new[]
        {
            "QuestionnaireResponse",
            "RiskAssessment",
            "Device",
            "Provenance"
        };

        Assert.DoesNotContain(domainAssembly.GetReferencedAssemblies(), assembly =>
            assembly.Name!.Contains("FHIR", StringComparison.OrdinalIgnoreCase) ||
            assembly.Name.Contains("HL7", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(domainAssembly.GetTypes(), type =>
            forbiddenResourceNames.Contains(type.Name, StringComparer.Ordinal));
        Assert.All(
            typeof(FhirExport).GetProperties()
                .Concat(typeof(FhirValidationResult).GetProperties()),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    private static FhirExport CreatePendingExport()
    {
        return FhirExport.CreatePending(
            CreateHistoryEvent(),
            FhirExportVersionMetadata.Create("future-release", "mapping-v1"),
            EntityId.New(),
            Utc(17));
    }

    private static FhirExport CreateGeneratedExport(string? checksum = null)
    {
        var export = CreatePendingExport();
        export.MarkGenerated(
            FhirArtifactMetadata.Create(
                "SHA-256",
                checksum ?? new string('a', 64),
                $"s3://private-beeexy/fhir/{export.Id}.json"),
            Utc(18));
        return export;
    }

    private static ClinicalHistoryEvent CreateHistoryEvent()
    {
        var session = PreTriageSession.CreateForPatient(
            EntityId.New(),
            EntityId.New(),
            Utc(20),
            Utc(12));
        var episode = PreTriageEpisode.CreateFrom(session, EntityId.New(), Utc(14));
        return ClinicalHistoryEvent.CreateCompletedPreTriage(episode, Utc(15));
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);
    }
}
