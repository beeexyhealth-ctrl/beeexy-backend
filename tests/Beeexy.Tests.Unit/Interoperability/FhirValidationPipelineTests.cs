using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class FhirValidationPipelineTests
{
    [Fact]
    public void Eligibility_CurrentSnapshotExposesEveryAuthoritativeBlockerCategory()
    {
        var generated = CreateGeneratedExport(releaseNeutral: true);

        var eligibility = new CurrentFhirValidationPrerequisiteEvaluator()
            .Evaluate(generated.Export);

        Assert.False(eligibility.IsEligible);
        Assert.Null(eligibility.Specification);
        Assert.Equal(
            [
                FhirValidationBlocker.ReleaseNeutralArtifact,
                FhirValidationBlocker.FhirReleaseUnresolved,
                FhirValidationBlocker.RequiredProfilesUnresolved,
                FhirValidationBlocker.ResourceIdentityAndReferencesUnresolved,
                FhirValidationBlocker.QuestionnaireLinkIdUnresolved,
                FhirValidationBlocker.QuestionnaireAnswerValueTranslationUnresolved,
                FhirValidationBlocker.MandatoryRiskAssessmentContentUnavailable,
                FhirValidationBlocker.RequiredResourceSetIncomplete,
                FhirValidationBlocker.NoApprovedValidationSpecification
            ],
            eligibility.Blockers);
        Assert.Equal(3, eligibility.BlockerCategories.Count);
    }

    [Fact]
    public void Specification_RequiresResolvedProfileApplicability()
    {
        Assert.Throws<ArgumentException>(() => FhirValidationSpecification.Create(
            "test-release",
            "test-map",
            FhirProfileResolution.Unresolved()));
    }

    [Fact]
    public async Task Validate_CurrentSnapshotIsBlockedWithoutLifecycleTransition()
    {
        var generated = CreateGeneratedExport(releaseNeutral: true);
        var validator = new FakeValidator(FhirValidatorExecutionResult.Unavailable());
        var transaction = new FakeTransaction(generated.Export);

        var result = await CreateUseCase(
            generated,
            transaction,
            new CurrentFhirValidationPrerequisiteEvaluator(),
            validator).ExecuteAsync(Command(generated.Export));

        Assert.Equal(FhirValidationPipelineStatus.Blocked, result.PipelineStatus);
        Assert.Equal(FhirExportStatus.Generated, result.Export.Status);
        Assert.NotNull(result.Eligibility);
        Assert.Null(result.ValidationResult);
        Assert.Equal(0, validator.CallCount);
        Assert.Equal(0, transaction.SaveCount);
        Assert.True(transaction.Committed);
    }

    [Fact]
    public async Task Validate_TamperedBytesFailIntegrityWithoutValidatorOrRewrite()
    {
        var generated = CreateGeneratedExport(releaseNeutral: false);
        var originalArtifact = generated.Export.Artifact;
        var validator = new FakeValidator(ValidResult());
        var store = new FakeStore(generated.Reference, "tampered"u8.ToArray());
        var transaction = new FakeTransaction(generated.Export);

        var result = await CreateUseCase(
            generated,
            transaction,
            EligibleEvaluator(generated.Export),
            validator,
            store).ExecuteAsync(Command(generated.Export));

        Assert.Equal(FhirValidationPipelineStatus.IntegrityFailed,
            result.PipelineStatus);
        Assert.Equal(FhirExportStatus.Generated, generated.Export.Status);
        Assert.Equal(originalArtifact, generated.Export.Artifact);
        Assert.Equal(0, validator.CallCount);
        Assert.Equal(0, store.WriteCount);
        Assert.Null(transaction.AddedResult);
    }

    [Fact]
    public async Task Validate_PendingExportIsRejectedBeforeArtifactAccess()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var pending = FhirExport.CreatePending(
            graph.HistoryEvent,
            FhirExportVersionMetadata.Create("test-release", "phase-6.6-test"),
            EntityId.New(),
            FhirSnapshotTestData.Utc(17));
        var transaction = new FakeTransaction(pending);
        var store = new FakeStore(
            FhirArtifactStorageReference.CreateNew(),
            "unused"u8.ToArray());

        await Assert.ThrowsAsync<FhirExportNotGeneratedException>(() =>
            CreateUseCase(
                new GeneratedFixture(pending, store.Reference, store.Bytes),
                transaction,
                new CurrentFhirValidationPrerequisiteEvaluator(),
                new FakeValidator(ValidResult()),
                store).ExecuteAsync(Command(pending)));

        Assert.Equal(0, store.ReadCount);
        Assert.False(transaction.Committed);
    }

    [Fact]
    public async Task Validate_DifferentPatientScopeDoesNotRevealExportOrReadArtifact()
    {
        var generated = CreateGeneratedExport(releaseNeutral: false);
        var store = new FakeStore(generated.Reference, generated.Bytes);
        var useCase = CreateUseCase(
            generated,
            new FakeTransaction(generated.Export),
            EligibleEvaluator(generated.Export),
            new FakeValidator(ValidResult()),
            store);

        await Assert.ThrowsAsync<FhirExportForValidationNotFoundException>(() =>
            useCase.ExecuteAsync(new ValidateFhirExportCommand(
                EntityId.New(),
                generated.Export.Id)));

        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task Validate_EligibleSuccessfulExecutionPersistsPassedEvidence()
    {
        var generated = CreateGeneratedExport(releaseNeutral: false);
        var validator = new FakeValidator(FhirValidatorExecutionResult.Valid(
            ValidatorMetadata(),
            [new(FhirValidationDiagnosticSeverity.Warning, "profile-note", "private")]));
        var transaction = new FakeTransaction(generated.Export);
        var artifact = generated.Export.Artifact;

        var result = await CreateUseCase(
            generated,
            transaction,
            EligibleEvaluator(generated.Export),
            validator).ExecuteAsync(Command(generated.Export));

        Assert.Equal(FhirValidationPipelineStatus.Validated, result.PipelineStatus);
        Assert.True(result.NewlyCompleted);
        Assert.Equal(FhirExportStatus.Validated, generated.Export.Status);
        Assert.Equal(FhirValidationOutcome.Passed,
            result.ValidationResult!.Outcome);
        Assert.Equal(FhirSnapshotTestData.Utc(19),
            result.ValidationResult.ValidatedAt);
        Assert.Equal(artifact, generated.Export.Artifact);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(generated.Bytes, validator.LastRequest!.ArtifactBytes.ToArray());
        Assert.Same(result.ValidationResult, transaction.AddedResult);
        Assert.Equal(1, transaction.SaveCount);
    }

    [Fact]
    public async Task Validate_EligibleInvalidExecutionPersistsFailedEvidence()
    {
        var generated = CreateGeneratedExport(releaseNeutral: false);
        var execution = FhirValidatorExecutionResult.Invalid(
            ValidatorMetadata(),
            [
                new(FhirValidationDiagnosticSeverity.Error, "invalid-reference", "raw"),
                new(FhirValidationDiagnosticSeverity.Warning, "profile-note", "raw")
            ]);

        var result = await CreateUseCase(
            generated,
            new FakeTransaction(generated.Export),
            EligibleEvaluator(generated.Export),
            new FakeValidator(execution)).ExecuteAsync(Command(generated.Export));

        Assert.Equal(FhirValidationPipelineStatus.ValidationFailed,
            result.PipelineStatus);
        Assert.Equal(FhirExportStatus.ValidationFailed, generated.Export.Status);
        Assert.Equal(1, result.ValidationResult!.ErrorCount);
        Assert.Equal(1, result.ValidationResult.WarningCount);
    }

    [Theory]
    [InlineData(FhirValidatorExecutionStatus.Unavailable,
        FhirValidationPipelineStatus.ValidatorUnavailable)]
    [InlineData(FhirValidatorExecutionStatus.UnsupportedSpecification,
        FhirValidationPipelineStatus.UnsupportedSpecification)]
    public async Task Validate_NonCompletedValidatorResultPreservesRetrySafeGeneratedState(
        FhirValidatorExecutionStatus executionStatus,
        FhirValidationPipelineStatus expectedStatus)
    {
        var generated = CreateGeneratedExport(releaseNeutral: false);
        var execution = executionStatus == FhirValidatorExecutionStatus.Unavailable
            ? FhirValidatorExecutionResult.Unavailable()
            : FhirValidatorExecutionResult.UnsupportedSpecification();
        var transaction = new FakeTransaction(generated.Export);

        var result = await CreateUseCase(
            generated,
            transaction,
            EligibleEvaluator(generated.Export),
            new FakeValidator(execution)).ExecuteAsync(Command(generated.Export));

        Assert.Equal(expectedStatus, result.PipelineStatus);
        Assert.Equal(FhirExportStatus.Generated, generated.Export.Status);
        Assert.Null(result.ValidationResult);
        Assert.Null(transaction.AddedResult);
        Assert.Equal(0, transaction.SaveCount);
    }

    [Fact]
    public async Task Validate_ValidatorExceptionIsNotExposedAndCanBeRetried()
    {
        var generated = CreateGeneratedExport(releaseNeutral: false);
        var validator = new FakeValidator(new InvalidOperationException(
            "patient free text and /private/storage/path"));

        var result = await CreateUseCase(
            generated,
            new FakeTransaction(generated.Export),
            EligibleEvaluator(generated.Export),
            validator).ExecuteAsync(Command(generated.Export));

        Assert.Equal(FhirValidationPipelineStatus.ValidatorUnavailable,
            result.PipelineStatus);
        Assert.Equal(FhirExportStatus.Generated, generated.Export.Status);
        Assert.DoesNotContain("patient", result.Diagnostics.Summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", result.Diagnostics.Summary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_FinalResultIsIdempotentAndCannotBeOverwritten()
    {
        var generated = CreateGeneratedExport(releaseNeutral: false);
        var firstTransaction = new FakeTransaction(generated.Export);
        var first = await CreateUseCase(
            generated,
            firstTransaction,
            EligibleEvaluator(generated.Export),
            new FakeValidator(ValidResult())).ExecuteAsync(Command(generated.Export));
        var validator = new FakeValidator(FhirValidatorExecutionResult.Invalid(
            ValidatorMetadata(),
            [new(FhirValidationDiagnosticSeverity.Error, "later-error", null)]));

        var repeated = await CreateUseCase(
            generated,
            new FakeTransaction(generated.Export, first.ValidationResult),
            EligibleEvaluator(generated.Export),
            validator).ExecuteAsync(Command(generated.Export));

        Assert.Equal(FhirValidationPipelineStatus.Validated,
            repeated.PipelineStatus);
        Assert.False(repeated.NewlyCompleted);
        Assert.Same(first.ValidationResult, repeated.ValidationResult);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public void DiagnosticSanitizer_DiscardsRawDetailsAndProviderCodes()
    {
        var diagnostics = new FhirValidationDiagnosticSanitizer().Sanitize(
        [
            new(FhirValidationDiagnosticSeverity.Error,
                " invalid reference/<script> ",
                "patient said sensitive free text at /private/path"),
            new(FhirValidationDiagnosticSeverity.Warning,
                new string('x', 100),
                "secret")
        ]);

        Assert.Equal(1, diagnostics.ErrorCount);
        Assert.Equal(1, diagnostics.WarningCount);
        Assert.Equal(
            ["fhir-validation-error", "fhir-validation-warning"],
            diagnostics.Codes);
        Assert.DoesNotContain("patient", diagnostics.Summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", diagnostics.Summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", string.Join(',', diagnostics.Codes),
            StringComparison.OrdinalIgnoreCase);
    }

    private static ValidateFhirExport CreateUseCase(
        GeneratedFixture fixture,
        FakeTransaction transaction,
        IFhirValidationPrerequisiteEvaluator evaluator,
        FakeValidator validator,
        FakeStore? store = null) => new(
            new FixedClock(FhirSnapshotTestData.Utc(19)),
            transaction,
            store ?? new FakeStore(fixture.Reference, fixture.Bytes),
            new FhirArtifactChecksumCalculator(),
            evaluator,
            validator,
            new FhirValidationDiagnosticSanitizer());

    private static GeneratedFixture CreateGeneratedExport(bool releaseNeutral)
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var versions = releaseNeutral
            ? FhirExportVersionMetadata.Create(
                FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
                "phase-6.6-test")
            : FhirExportVersionMetadata.Create("test-release", "phase-6.6-test");
        var export = FhirExport.CreatePending(
            graph.HistoryEvent,
            versions,
            EntityId.New(),
            FhirSnapshotTestData.Utc(17));
        var bytes = releaseNeutral
            ? "release-neutral-snapshot"u8.ToArray()
            : "official-test-fhir-json"u8.ToArray();
        var reference = FhirArtifactStorageReference.CreateNew();
        export.MarkGenerated(
            FhirArtifactMetadata.Create(
                FhirArtifactChecksumCalculator.Algorithm,
                new FhirArtifactChecksumCalculator().Calculate(bytes),
                reference.PrivateUri),
            FhirSnapshotTestData.Utc(18));
        return new GeneratedFixture(export, reference, bytes);
    }

    private static IFhirValidationPrerequisiteEvaluator EligibleEvaluator(
        FhirExport export) => new FixedEvaluator(FhirValidationEligibility.Eligible(
            FhirValidationSpecification.Create(
                export.FhirVersion,
                export.MappingVersion,
                FhirProfileResolution.NotApplicable())));

    private static FhirValidatorExecutionResult ValidResult() =>
        FhirValidatorExecutionResult.Valid(ValidatorMetadata());

    private static FhirValidatorMetadata ValidatorMetadata() =>
        FhirValidatorMetadata.Create("controlled-test-validator", "test-v1");

    private static ValidateFhirExportCommand Command(FhirExport export) =>
        new(export.PatientProfileId, export.Id);

    private sealed record GeneratedFixture(
        FhirExport Export,
        FhirArtifactStorageReference Reference,
        byte[] Bytes);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedEvaluator(FhirValidationEligibility eligibility)
        : IFhirValidationPrerequisiteEvaluator
    {
        public FhirValidationEligibility Evaluate(FhirExport export) => eligibility;
    }

    private sealed class FakeValidator : IFhirValidator
    {
        private readonly FhirValidatorExecutionResult? result;
        private readonly Exception? exception;

        public FakeValidator(FhirValidatorExecutionResult result)
        {
            this.result = result;
        }

        public FakeValidator(Exception exception)
        {
            this.exception = exception;
        }

        public int CallCount { get; private set; }

        public FhirValidatorRequest? LastRequest { get; private set; }

        public Task<FhirValidatorExecutionResult> ValidateAsync(
            FhirValidatorRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return exception is null
                ? Task.FromResult(result!)
                : Task.FromException<FhirValidatorExecutionResult>(exception);
        }
    }

    private sealed class FakeStore(
        FhirArtifactStorageReference reference,
        byte[] bytes) : IFhirArtifactStore
    {
        public FhirArtifactStorageReference Reference { get; } = reference;

        public byte[] Bytes { get; } = bytes;

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public Task StoreImmutableAsync(FhirArtifactStorageReference value,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            throw new InvalidOperationException("Validation must not write artifacts.");
        }

        public Task<byte[]> ReadAsync(FhirArtifactStorageReference value,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            Assert.Equal(Reference, value);
            return Task.FromResult(Bytes.ToArray());
        }

        public Task<bool> DeleteAsync(FhirArtifactStorageReference value,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Validation must not delete artifacts.");
    }

    private sealed class FakeTransaction(
        FhirExport export,
        FhirValidationResult? existingResult = null)
        : IFhirExportValidationTransaction
    {
        public FhirValidationResult? AddedResult { get; private set; }

        public int SaveCount { get; private set; }

        public bool Committed { get; private set; }

        public Task BeginAsync(EntityId fhirExportId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FhirExportValidationState?> LoadAsync(
            EntityId patientProfileId,
            EntityId fhirExportId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FhirExportValidationState?>(
                patientProfileId == export.PatientProfileId &&
                fhirExportId == export.Id
                    ? new FhirExportValidationState(export, existingResult)
                    : null);

        public void Add(FhirValidationResult result) => AddedResult = result;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
