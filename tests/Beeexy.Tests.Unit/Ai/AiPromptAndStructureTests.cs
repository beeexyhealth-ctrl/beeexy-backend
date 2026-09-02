using System.Text.Json;
using Beeexy.Application.Ai;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase102")]
[Trait("Category", "Phase108")]
public sealed class AiPromptAndStructureTests
{
    [Fact]
    public void FutureWorkloadsHaveDistinctStableContractIdentifiersWithoutPromptContent()
    {
        Assert.Equal("ai-conversation", AiWorkloadIdentifiers.Conversation);
        Assert.Equal("ai-second-opinion", AiWorkloadIdentifiers.SecondOpinion);
        Assert.Equal("ai-conversation", AiPromptContractIdentifiers.Conversation);
        Assert.Equal("ai-second-opinion", AiPromptContractIdentifiers.SecondOpinion);
        Assert.Equal("ai-safety-fallback", AiPromptContractIdentifiers.SafetyFallback);
        Assert.Equal(3, new HashSet<string>
        {
            AiPromptContractIdentifiers.Conversation,
            AiPromptContractIdentifiers.SecondOpinion,
            AiPromptContractIdentifiers.SafetyFallback
        }.Count);
    }

    [Fact]
    public void PromptResolver_ResolvesRequestedVersionDeterministically()
    {
        var v1 = new PromptContract("contract", "v1", "system-v1");
        var v2 = new PromptContract("contract", "v2", "system-v2");
        var resolver = new AiPromptResolver([v1, v2]);

        var first = resolver.Resolve(v2.Identity, "private input");
        var second = resolver.Resolve(v2.Identity, "private input");

        Assert.Equal("system-v2", first.SystemInstructions);
        Assert.Equal(first, second);
        Assert.Equal("contract@v2", first.Identity.PersistenceValue);
    }

    [Fact]
    public void PromptResolver_RejectsDuplicateAndUnknownVersions()
    {
        var first = new PromptContract("contract", "v1", "one");
        var duplicate = new PromptContract("contract", "v1", "two");

        Assert.Throws<ArgumentException>(() => new AiPromptResolver([first, duplicate]));
        var resolver = new AiPromptResolver([first]);
        Assert.Throws<AiPromptContractNotFoundException>(() =>
            resolver.Resolve(new AiPromptIdentity("contract", "v2"), "input"));
    }

    [Theory]
    [InlineData("not-json", AiStructuralValidationIssue.InvalidJson)]
    [InlineData("[]", AiStructuralValidationIssue.InvalidRootType)]
    [InlineData("{}", AiStructuralValidationIssue.MissingRequiredField)]
    [InlineData("{\"schemaVersion\":1,\"answer\":\"ok\"}",
        AiStructuralValidationIssue.InvalidFieldType)]
    [InlineData("{\"schemaVersion\":\"v2\",\"answer\":\"ok\"}",
        AiStructuralValidationIssue.SchemaVersionMismatch)]
    [InlineData("{\"schemaVersion\":\"v1\",\"answer\":1}",
        AiStructuralValidationIssue.InvalidFieldType)]
    public void StructuredValidator_DistinguishesTechnicalFailures(
        string content,
        AiStructuralValidationIssue expected)
    {
        var identity = new AiStructuredResultIdentity("generic", "v1");
        var validator = new AiStructuredResultValidator([new Schema(identity)]);

        var result = validator.Validate(identity, content);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Issue);
    }

    [Fact]
    public void StructuredValidator_RejectsUnsupportedSchemaAndAcceptsValidObject()
    {
        var identity = new AiStructuredResultIdentity("generic", "v1");
        var validator = new AiStructuredResultValidator([new Schema(identity)]);

        var unsupported = validator.Validate(
            new AiStructuredResultIdentity("generic", "v2"),
            "{}");
        var valid = validator.Validate(
            identity,
            "{\"schemaVersion\":\"v1\",\"answer\":\"ok\"}");

        Assert.Equal(AiStructuralValidationIssue.UnsupportedSchema, unsupported.Issue);
        Assert.True(valid.IsValid);
        Assert.Null(valid.Issue);
    }

    private sealed class PromptContract(
        string identifier,
        string version,
        string systemInstructions) : IAiPromptContract
    {
        public AiPromptIdentity Identity { get; } = new(identifier, version);

        public AiResolvedPrompt Build(string preparedInput) => new(
            Identity,
            systemInstructions,
            preparedInput);
    }

    private sealed class Schema(AiStructuredResultIdentity identity)
        : IAiStructuredResultSchema
    {
        public AiStructuredResultIdentity Identity { get; } = identity;

        public AiStructuralValidationResult Validate(JsonElement result)
        {
            if (!result.TryGetProperty("schemaVersion", out var version))
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.MissingRequiredField);
            }

            if (version.ValueKind != JsonValueKind.String)
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.InvalidFieldType);
            }

            if (version.GetString() != Identity.Version)
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.SchemaVersionMismatch);
            }

            return result.TryGetProperty("answer", out var answer) &&
                answer.ValueKind == JsonValueKind.String
                    ? AiStructuralValidationResult.Valid
                    : AiStructuralValidationResult.Invalid(
                        result.TryGetProperty("answer", out _)
                            ? AiStructuralValidationIssue.InvalidFieldType
                            : AiStructuralValidationIssue.MissingRequiredField);
        }
    }
}
