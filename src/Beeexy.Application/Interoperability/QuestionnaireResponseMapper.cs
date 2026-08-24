using System.Text.Json;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Interoperability;

public enum QuestionnaireResponseSourceAnswerKind
{
    Object = 1,
    Array = 2,
    String = 3,
    Number = 4,
    Boolean = 5
}

public sealed record QuestionnaireResponseAnswerRepresentation(
    EntityId SourceAnswerId,
    string SourceAnswerSchemaJson,
    string SourceAnswerJson,
    QuestionnaireResponseSourceAnswerKind SourceKind,
    DateTimeOffset RecordedAt);

public sealed record QuestionnaireResponseItemRepresentation(
    EntityId SourceQuestionId,
    string SourceQuestionCode,
    string Text,
    int DisplayOrder,
    string? LinkId,
    QuestionnaireResponseAnswerRepresentation Answer);

public sealed class QuestionnaireResponseRepresentation
{
    internal QuestionnaireResponseRepresentation(
        QuestionnaireResponseMappingInput source,
        FhirMappingSpecificationIdentity mappingSpecification,
        IReadOnlyList<QuestionnaireResponseItemRepresentation> items,
        IReadOnlyList<FhirUnresolvedMappingRequirement> unresolvedRequirements)
    {
        SourceClinicalHistoryEventId = source.SourceClinicalHistoryEventId;
        SourceEpisodeId = source.EpisodeId;
        SourcePatientProfileId = source.PatientProfileId;
        SourceQuestionnaireVersionId = source.QuestionnaireVersionId;
        SourceQuestionnaireCode = source.QuestionnaireCode;
        SourceQuestionnaireVersion = source.QuestionnaireVersion;
        SourceQuestionnaireContentHash = source.QuestionnaireContentHash;
        AuthoredAt = source.AuthoredAt;
        MappingSpecification = mappingSpecification;
        Items = items;
        UnresolvedRequirements = unresolvedRequirements;
    }

    public FhirConceptualResource Resource =>
        FhirConceptualResource.QuestionnaireResponse;

    public string Status => "completed";

    public EntityId SourceClinicalHistoryEventId { get; }

    public EntityId SourceEpisodeId { get; }

    public EntityId SourcePatientProfileId { get; }

    public string? SubjectReference => null;

    public string? LogicalId => null;

    public EntityId SourceQuestionnaireVersionId { get; }

    public string SourceQuestionnaireCode { get; }

    public string SourceQuestionnaireVersion { get; }

    public string SourceQuestionnaireContentHash { get; }

    public string? QuestionnaireReference => null;

    public DateTimeOffset AuthoredAt { get; }

    public FhirMappingSpecificationIdentity MappingSpecification { get; }

    public IReadOnlyList<QuestionnaireResponseItemRepresentation> Items { get; }

    public IReadOnlyList<FhirUnresolvedMappingRequirement> UnresolvedRequirements { get; }

    public bool CanSerializeAsFhir => false;
}

public sealed class QuestionnaireResponseMapper :
    IFhirMapper<QuestionnaireResponseMappingInput, QuestionnaireResponseRepresentation>
{
    private readonly FhirMappingSpecificationIdentity _mappingSpecification;

    public QuestionnaireResponseMapper(
        FhirMappingSpecificationIdentity mappingSpecification)
    {
        ArgumentNullException.ThrowIfNull(mappingSpecification);
        _mappingSpecification = mappingSpecification;
    }

    public QuestionnaireResponseRepresentation Map(
        QuestionnaireResponseMappingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Answers.Count == 0)
        {
            throw new FhirMappingInputException(
                "QuestionnaireResponse generation requires at least one source answer.");
        }

        var duplicateQuestion = input.Answers
            .GroupBy(answer => answer.QuestionId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateQuestion is not null)
        {
            throw new FhirMappingInputException(
                "QuestionnaireResponse generation found multiple source answers for one question.");
        }

        var items = input.Answers
            .OrderBy(answer => answer.DisplayOrder)
            .ThenBy(answer => answer.QuestionId.Value)
            .Select(MapItem)
            .ToArray();

        return new QuestionnaireResponseRepresentation(
            input,
            _mappingSpecification,
            Array.AsReadOnly(items),
            ResolveUnresolvedRequirements(_mappingSpecification));
    }

    private static QuestionnaireResponseItemRepresentation MapItem(
        QuestionnaireResponseAnswerInput source)
    {
        if (string.IsNullOrWhiteSpace(source.AnswerSchemaJson))
        {
            throw new FhirMappingInputException(
                "A source answer is missing its frozen answer schema.");
        }

        try
        {
            using var _ = JsonDocument.Parse(source.AnswerSchemaJson);
        }
        catch (JsonException)
        {
            throw new FhirMappingInputException(
                "A source answer has an invalid frozen answer schema.");
        }

        if (string.IsNullOrWhiteSpace(source.AnswerJson))
        {
            throw new FhirMappingInputException(
                "A submitted source answer is not valid JSON.");
        }

        QuestionnaireResponseSourceAnswerKind sourceKind;
        try
        {
            using var document = JsonDocument.Parse(source.AnswerJson);
            sourceKind = document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => QuestionnaireResponseSourceAnswerKind.Object,
                JsonValueKind.Array => QuestionnaireResponseSourceAnswerKind.Array,
                JsonValueKind.String => QuestionnaireResponseSourceAnswerKind.String,
                JsonValueKind.Number => QuestionnaireResponseSourceAnswerKind.Number,
                JsonValueKind.True or JsonValueKind.False =>
                    QuestionnaireResponseSourceAnswerKind.Boolean,
                _ => throw new FhirMappingInputException(
                    "A submitted source answer has no supported truthful representation.")
            };
        }
        catch (JsonException)
        {
            throw new FhirMappingInputException(
                "A submitted source answer is not valid JSON.");
        }

        return new QuestionnaireResponseItemRepresentation(
            source.QuestionId,
            source.QuestionCode,
            source.PromptText,
            source.DisplayOrder,
            LinkId: null,
            new QuestionnaireResponseAnswerRepresentation(
                source.AnswerId,
                source.AnswerSchemaJson,
                source.AnswerJson,
                sourceKind,
                source.RecordedAt));
    }

    private static IReadOnlyList<FhirUnresolvedMappingRequirement>
        ResolveUnresolvedRequirements(
            FhirMappingSpecificationIdentity mappingSpecification)
    {
        var unresolved = FhirRepresentationRequirements.From(mappingSpecification);

        unresolved.AddRange(
        [
            FhirUnresolvedMappingRequirement.QuestionnaireResponseResourceIdentity,
            FhirUnresolvedMappingRequirement.PatientResourceIdentity,
            FhirUnresolvedMappingRequirement.QuestionnaireResourceIdentityAndVersionEncoding,
            FhirUnresolvedMappingRequirement.QuestionnaireItemLinkIdStrategy,
            FhirUnresolvedMappingRequirement.QuestionnaireAnswerTypeTranslation
        ]);
        return unresolved.AsReadOnly();
    }
}
