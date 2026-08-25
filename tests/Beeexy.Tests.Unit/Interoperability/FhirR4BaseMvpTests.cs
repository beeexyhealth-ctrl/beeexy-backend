using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;
using Beeexy.Infrastructure.Interoperability;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class FhirR4BaseMvpTests
{
    [Fact]
    public void Serialize_ProducesDeterministicClosedR4CollectionBundle()
    {
        var snapshot = FhirSnapshotTestData.CreateR4Snapshot();
        var serializer = new FirelyFhirR4BundleSerializer();

        Assert.True(snapshot.IsCompleteFhirExport);
        Assert.True(snapshot.CanBeFhirValidated);
        Assert.Empty(snapshot.UnresolvedRequirements);
        Assert.True(snapshot.RiskAssessmentBoundary.IsConcreteGenerationBlocked);

        var first = serializer.Serialize(snapshot);
        var second = serializer.Serialize(snapshot);

        Assert.Equal(first, second);
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal("Bundle", root.GetProperty("resourceType").GetString());
        Assert.Equal("collection", root.GetProperty("type").GetString());
        var entries = root.GetProperty("entry").EnumerateArray().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal(
            ["QuestionnaireResponse", "Device", "Provenance"],
            entries.Select(value => value.GetProperty("resource")
                .GetProperty("resourceType").GetString()));
        Assert.All(entries, entry =>
        {
            var resource = entry.GetProperty("resource");
            Assert.Equal(
                $"urn:uuid:{resource.GetProperty("id").GetString()}",
                entry.GetProperty("fullUrl").GetString());
            Assert.False(resource.TryGetProperty("meta", out _));
        });

        var response = entries[0].GetProperty("resource");
        Assert.Equal("completed", response.GetProperty("status").GetString());
        Assert.False(response.TryGetProperty("subject", out _));
        Assert.False(response.TryGetProperty("questionnaire", out _));
        var item = Assert.Single(response.GetProperty("item").EnumerateArray());
        Assert.Equal("SYMPTOM_TEXT", item.GetProperty("linkId").GetString());
        Assert.Equal(
            "historical answer",
            Assert.Single(item.GetProperty("answer").EnumerateArray())
                .GetProperty("valueString").GetString());

        var provenance = entries[2].GetProperty("resource");
        var knownUrls = entries.Select(value => value.GetProperty("fullUrl").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            Assert.Single(provenance.GetProperty("target").EnumerateArray())
                .GetProperty("reference").GetString(),
            knownUrls);
        Assert.Contains(
            Assert.Single(provenance.GetProperty("agent").EnumerateArray())
                .GetProperty("who").GetProperty("reference").GetString(),
            knownUrls);
        Assert.DoesNotContain("Patient", Encoding.UTF8.GetString(first));
        Assert.DoesNotContain("RiskAssessment", Encoding.UTF8.GetString(first));
        Assert.DoesNotContain("Composition", Encoding.UTF8.GetString(first));
    }

    [Fact]
    public async Task Validate_ExactGeneratedBytesPassAndBrokenReferenceFails()
    {
        var bytes = new FirelyFhirR4BundleSerializer().Serialize(
            FhirSnapshotTestData.CreateR4Snapshot());
        var validator = new FirelyFhirR4Validator();

        var valid = await validator.ValidateAsync(Request(bytes));

        Assert.Equal(FhirValidatorExecutionStatus.Valid, valid.Status);
        Assert.Equal("Firely .NET SDK R4 POCO validator", valid.Validator!.Name);
        Assert.Contains(valid.Diagnostics,
            value => value.Code == "external-terminology-not-executed");

        var root = JsonNode.Parse(bytes)!.AsObject();
        var provenance = root["entry"]!.AsArray()[2]!["resource"]!.AsObject();
        provenance["agent"]!.AsArray()[0]!["who"]!["reference"] =
            "urn:uuid:00000000-0000-0000-0000-000000000000";
        var invalidBytes = Encoding.UTF8.GetBytes(root.ToJsonString());

        var invalid = await validator.ValidateAsync(Request(invalidBytes));

        Assert.Equal(FhirValidatorExecutionStatus.Invalid, invalid.Status);
        Assert.Contains(invalid.Diagnostics,
            value => value.Code == "unresolved-bundle-reference");

        root = JsonNode.Parse(bytes)!.AsObject();
        root["entry"]!.AsArray()[0]!["resource"]!["status"] = "in-progress";
        invalid = await validator.ValidateAsync(Request(
            Encoding.UTF8.GetBytes(root.ToJsonString())));

        Assert.Equal(FhirValidatorExecutionStatus.Invalid, invalid.Status);
        Assert.Contains(invalid.Diagnostics,
            value => value.Code == "invalid-r4-mvp-questionnaire-response");
    }

    [Fact]
    public void Serialize_UsesFrozenAnswerTypesWithoutInventingCodings()
    {
        var graph = FhirSnapshotTestData.CreateTypedGraph(
            Answer("TEXT", "FREE_TEXT", "\"café\""),
            Answer("CHOICE", "SINGLE_CHOICE", "\"SUDDEN\""),
            Answer("MULTI", "MULTIPLE_CHOICE", "{\"values\":[\"A\",\"B\"]}"),
            Answer("SCALE", "INTEGER_SCALE", "{\"value\":7}"),
            Answer("NEGATIVE", "BOOLEAN", "false"),
            Answer("DURATION", "DURATION", "{\"value\":2,\"unit\":\"DAYS\"}"),
            Answer("TEMPERATURE", "TEMPERATURE",
                "{\"value\":38.5,\"unit\":\"CELSIUS\"}"),
            Answer("SYMPTOM", "SYMPTOM_SELECTION", "\"ABDOMINAL_PAIN\""));
        var bytes = new FirelyFhirR4BundleSerializer().Serialize(
            FhirSnapshotTestData.CreateR4Snapshot(graph));
        using var document = JsonDocument.Parse(bytes);
        var items = document.RootElement.GetProperty("entry")[0]
            .GetProperty("resource").GetProperty("item").EnumerateArray()
            .ToDictionary(value => value.GetProperty("linkId").GetString()!);

        Assert.Equal("café", Value(items["TEXT"]).GetProperty("valueString").GetString());
        Assert.Equal("SUDDEN", Value(items["CHOICE"])
            .GetProperty("valueString").GetString());
        Assert.Equal(["A", "B"], items["MULTI"].GetProperty("answer")
            .EnumerateArray().Select(value => value.GetProperty("valueString").GetString()));
        Assert.Equal(7, Value(items["SCALE"]).GetProperty("valueInteger").GetInt32());
        Assert.False(Value(items["NEGATIVE"]).GetProperty("valueBoolean").GetBoolean());
        Assert.Equal(2, Value(items["DURATION"]).GetProperty("valueQuantity")
            .GetProperty("value").GetDecimal());
        Assert.Equal("DAYS", Value(items["DURATION"]).GetProperty("valueQuantity")
            .GetProperty("unit").GetString());
        Assert.Equal(38.5m, Value(items["TEMPERATURE"]).GetProperty("valueQuantity")
            .GetProperty("value").GetDecimal());
        Assert.Equal("ABDOMINAL_PAIN", Value(items["SYMPTOM"])
            .GetProperty("valueString").GetString());
        Assert.DoesNotContain("valueCoding", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Serialize_DeclaredTypeMismatchFailsInsteadOfGuessing()
    {
        var graph = FhirSnapshotTestData.CreateTypedGraph(
            Answer("SCALE", "INTEGER_SCALE", "\"seven\""));

        Assert.Throws<FhirR4BundleSerializationException>(() =>
            new FirelyFhirR4BundleSerializer().Serialize(
                FhirSnapshotTestData.CreateR4Snapshot(graph)));
    }

    [Fact]
    public async Task Validate_MalformedAndWrongResourceSetAreInvalid()
    {
        var validator = new FirelyFhirR4Validator();

        var malformed = await validator.ValidateAsync(Request("{"u8.ToArray()));
        var emptyCollection = await validator.ValidateAsync(Request(
            "{\"resourceType\":\"Bundle\",\"type\":\"collection\"}"u8.ToArray()));

        Assert.Equal(FhirValidatorExecutionStatus.Invalid, malformed.Status);
        Assert.Equal(FhirValidatorExecutionStatus.Invalid, emptyCollection.Status);
    }

    [Fact]
    public async Task Validate_AndreaDerivedReferenceFixturePassesBaseR4()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "beeexy-r4-base-mvp-reference.json"));

        var result = await new FirelyFhirR4Validator().ValidateAsync(Request(bytes));

        Assert.Equal(FhirValidatorExecutionStatus.Valid, result.Status);
    }

    [Fact]
    public void Eligibility_IsStrictlyLimitedToFrozenR4BaseIdentity()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var export = Beeexy.Domain.Interoperability.FhirExport.CreatePending(
            graph.HistoryEvent,
            FhirR4BaseMvp.MappingSpecification().ToExportVersionMetadata(),
            EntityId.New(),
            FhirSnapshotTestData.Utc(18));
        var evaluator = new CurrentFhirValidationPrerequisiteEvaluator();

        var result = evaluator.Evaluate(export);

        Assert.True(result.IsEligible);
        Assert.Equal(FhirR4BaseMvp.FhirRelease, result.Specification!.FhirRelease);
        Assert.Equal(FhirProfileResolutionStatus.NotApplicable,
            result.Specification.ProfileResolution.Status);
    }

    [Fact]
    public async Task Pipeline_RealValidatorTransitionsValidAndInvalidArtifacts()
    {
        var validBytes = new FirelyFhirR4BundleSerializer().Serialize(
            FhirSnapshotTestData.CreateR4Snapshot());
        var invalidBytes =
            "{\"resourceType\":\"Bundle\",\"type\":\"collection\"}"u8.ToArray();

        var valid = await ValidateThroughPipeline(validBytes);
        var invalid = await ValidateThroughPipeline(invalidBytes);

        Assert.Equal(FhirValidationPipelineStatus.Validated, valid.PipelineStatus);
        Assert.Equal(FhirExportStatus.Validated, valid.Export.Status);
        Assert.Equal(FhirValidationOutcome.Passed, valid.ValidationResult!.Outcome);
        Assert.Equal(FhirValidationPipelineStatus.ValidationFailed,
            invalid.PipelineStatus);
        Assert.Equal(FhirExportStatus.ValidationFailed, invalid.Export.Status);
        Assert.Equal(FhirValidationOutcome.Failed, invalid.ValidationResult!.Outcome);
    }

    private static FhirValidatorRequest Request(byte[] bytes) => new(
        EntityId.New(),
        bytes,
        FhirArtifactChecksumCalculator.Algorithm,
        new FhirArtifactChecksumCalculator().Calculate(bytes),
        FhirR4BaseMvp.ValidationSpecification());

    private static FhirSnapshotTestData.TypedAnswer Answer(
        string code,
        string type,
        string json) => new(
            code,
            $"{{\"answer\":{{\"type\":\"{type}\"}},\"priority\":\"ORDINARY\"}}",
            json);

    private static JsonElement Value(JsonElement item) =>
        Assert.Single(item.GetProperty("answer").EnumerateArray());

    private static async Task<ValidateFhirExportResult> ValidateThroughPipeline(
        byte[] bytes)
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var calculator = new FhirArtifactChecksumCalculator();
        var reference = FhirArtifactStorageReference.CreateNew();
        var export = FhirExport.CreatePending(
            graph.HistoryEvent,
            FhirR4BaseMvp.MappingSpecification().ToExportVersionMetadata(),
            EntityId.New(),
            FhirSnapshotTestData.Utc(18));
        export.MarkGenerated(
            FhirArtifactMetadata.Create(
                FhirArtifactChecksumCalculator.Algorithm,
                calculator.Calculate(bytes),
                reference.PrivateUri),
            FhirSnapshotTestData.Utc(18));
        var useCase = new ValidateFhirExport(
            new FixedClock(FhirSnapshotTestData.Utc(19)),
            new ValidationTransaction(export),
            new ReadOnlyArtifactStore(bytes),
            calculator,
            new CurrentFhirValidationPrerequisiteEvaluator(),
            new FirelyFhirR4Validator(),
            new FhirValidationDiagnosticSanitizer());
        return await useCase.ExecuteAsync(new ValidateFhirExportCommand(
            graph.PatientId,
            export.Id));
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }

    private sealed class ValidationTransaction(FhirExport export)
        : IFhirExportValidationTransaction
    {
        public FhirValidationResult? Added { get; private set; }

        public Task BeginAsync(EntityId fhirExportId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FhirExportValidationState?> LoadAsync(
            EntityId patientProfileId,
            EntityId fhirExportId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FhirExportValidationState?>(
                new FhirExportValidationState(export, Added));

        public void Add(FhirValidationResult result) => Added = result;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ReadOnlyArtifactStore(byte[] bytes) : IFhirArtifactStore
    {
        public Task StoreImmutableAsync(FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]> ReadAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(bytes.ToArray());

        public Task<bool> DeleteAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
