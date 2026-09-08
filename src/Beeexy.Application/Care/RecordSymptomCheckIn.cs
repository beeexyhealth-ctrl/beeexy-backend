using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Care;

public sealed class RecordSymptomCheckIn(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    ISymptomDiaryEpisodeReadRepository episodeRepository,
    ISymptomDiaryContentProvider contentProvider,
    SymptomDiaryPackageValidator packageValidator,
    SymptomDiaryAnswerStructureValidator answerValidator,
    ISymptomCheckInTransaction transaction,
    ISymptomCheckInAuditLogger auditLogger)
{
    public async Task<RecordSymptomCheckInResult> ExecuteAsync(
        RecordSymptomCheckInCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.EpisodeId.Value == Guid.Empty)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var eligibleEpisode = await episodeRepository.GetEligibleAsync(
            command.EpisodeId,
            cancellationToken);
        if (eligibleEpisode is null)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var authorization = await authorizePatientAccess.ExecuteAsync(
            eligibleEpisode.PatientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        ValidateEnvelope(command);
        await transaction.BeginAsync(
            command.EpisodeId,
            command.IdempotencyKey,
            cancellationToken);

        var source = await transaction.FindEpisodeAsync(
            command.EpisodeId,
            cancellationToken);
        if (source is null ||
            source.PatientProfileId != eligibleEpisode.PatientProfileId ||
            source.Pathway != eligibleEpisode.Pathway)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        authorization = await authorizePatientAccess.ExecuteForPatientUpdateAsync(
            source.PatientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var content = await GetEligibleExactPackageAsync(
            command.PackageVersionId,
            source.Pathway,
            cancellationToken);
        IReadOnlyList<ValidatedSymptomDiaryAnswer> answers;
        try
        {
            answers = answerValidator.Validate(content.Definition, command.Answers);
        }
        catch (SymptomDiaryPackageIntegrityException)
        {
            throw new SymptomDiaryContentUnavailableException();
        }

        var requestHash = SymptomCheckInRequestHashCalculator.Calculate(
            command.PackageVersionId,
            answers);
        var existing = await transaction.FindExistingAsync(
            command.EpisodeId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            EnsureReplayMatches(existing, command.PackageVersionId, requestHash);
            await transaction.CommitAsync(cancellationToken);
            auditLogger.Recorded(
                current.Account.Id,
                command.EpisodeId,
                command.PackageVersionId,
                existing.Id,
                answers.Count,
                authorization.Reason,
                newlyCreated: false,
                existing.CreatedAt);
            return ToResult(source, content, existing, answers, newlyCreated: false);
        }

        var package = await transaction.FindPackageAsync(
            command.PackageVersionId,
            cancellationToken);
        if (package is null ||
            package.Pathway != source.Pathway ||
            package.CanonicalContentHash != content.CanonicalContentHash)
        {
            throw new SymptomDiaryContentUnavailableException();
        }

        var domainAnswers = BindDomainQuestions(package, answers);
        var checkIn = SymptomCheckIn.Create(
            source.Episode,
            source.FrozenQuestionnaire,
            package,
            current.Account.Id,
            command.IdempotencyKey,
            requestHash,
            NormalizePostgreSqlInstant(clock.UtcNow),
            domainAnswers);
        transaction.Add(checkIn);
        var saveResult = await transaction.SaveAsync(checkIn, cancellationToken);
        EnsureReplayMatches(saveResult.CheckIn, command.PackageVersionId, requestHash);
        await transaction.CommitAsync(cancellationToken);

        auditLogger.Recorded(
            current.Account.Id,
            command.EpisodeId,
            command.PackageVersionId,
            saveResult.CheckIn.Id,
            answers.Count,
            authorization.Reason,
            saveResult.NewlyCreated,
            saveResult.CheckIn.CreatedAt);
        return ToResult(
            source,
            content,
            saveResult.CheckIn,
            answers,
            saveResult.NewlyCreated);
    }

    private async Task<SymptomDiaryPackageContent> GetEligibleExactPackageAsync(
        EntityId packageVersionId,
        ClinicalPathwayCode pathway,
        CancellationToken cancellationToken)
    {
        SymptomDiaryPackageContent? content;
        try
        {
            content = await contentProvider.GetExactPackageAsync(
                packageVersionId,
                cancellationToken);
            if (content is null ||
                content.PackageVersionId != packageVersionId ||
                content.Definition.Pathway != pathway ||
                !packageValidator.IsDisplayEligible(content.Definition))
            {
                throw new SymptomDiaryContentUnavailableException();
            }
        }
        catch (SymptomDiaryPackageIntegrityException)
        {
            throw new SymptomDiaryContentUnavailableException();
        }
        catch (SymptomDiaryPackageValidationException)
        {
            throw new SymptomDiaryContentUnavailableException();
        }

        return content;
    }

    private void EnsureReplayMatches(
        SymptomCheckIn existing,
        EntityId packageVersionId,
        SymptomDiarySha256 requestHash)
    {
        if (existing.PackageVersionId == packageVersionId &&
            existing.CanonicalRequestHash == requestHash)
        {
            return;
        }

        auditLogger.IdempotencyConflict(
            existing.EpisodeId,
            packageVersionId,
            existing.IdempotencyKey,
            clock.UtcNow);
        throw new SymptomCheckInIdempotencyConflictException();
    }

    private static IReadOnlyList<SymptomCheckInAnswerInput> BindDomainQuestions(
        SymptomDiaryPackageVersion package,
        IReadOnlyList<ValidatedSymptomDiaryAnswer> answers)
    {
        var questions = package.Questions.ToDictionary(
            question => question.Code,
            question => question);
        var result = new List<SymptomCheckInAnswerInput>(answers.Count);
        foreach (var answer in answers)
        {
            if (!questions.TryGetValue(answer.Question.Code, out var question) ||
                question.PackageVersionId != package.Id ||
                question.SourceOrder != answer.Question.SourceOrder)
            {
                throw new SymptomDiaryContentUnavailableException();
            }

            result.Add(new SymptomCheckInAnswerInput(
                question,
                answer.SubmittedValueJson));
        }

        return result;
    }

    private static RecordSymptomCheckInResult ToResult(
        SymptomCheckInEpisodeSource source,
        SymptomDiaryPackageContent content,
        SymptomCheckIn checkIn,
        IReadOnlyList<ValidatedSymptomDiaryAnswer> answers,
        bool newlyCreated) => new(
            checkIn.Id,
            checkIn.EpisodeId,
            checkIn.PackageVersionId,
            source.Pathway,
            checkIn.CreatedAt,
            content,
            answers,
            newlyCreated);

    private static void ValidateEnvelope(RecordSymptomCheckInCommand command)
    {
        if (command.HasUnsupportedFields)
        {
            throw new RequestValidationException(
                "symptom_diary.unsupported_fields",
                "The symptom-diary check-in request contains unsupported fields.");
        }

        if (command.PackageVersionId.Value == Guid.Empty ||
            command.IdempotencyKey.Value == Guid.Empty)
        {
            throw new RequestValidationException(
                "symptom_diary.identifiers_required",
                "Non-empty packageVersionId and idempotencyKey values are required.");
        }

        if (!command.AnswersProvided)
        {
            throw new SymptomDiaryAnswerValidationException();
        }
    }

    private static DateTimeOffset NormalizePostgreSqlInstant(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

public sealed record RecordSymptomCheckInCommand(
    EntityId EpisodeId,
    EntityId PackageVersionId,
    EntityId IdempotencyKey,
    IReadOnlyList<SymptomDiarySubmittedAnswer> Answers,
    bool AnswersProvided = true,
    bool HasUnsupportedFields = false);

public sealed record SymptomDiarySubmittedAnswer(
    string? QuestionCode,
    JsonElement Value,
    bool HasUnsupportedFields = false);

public sealed record ValidatedSymptomDiaryAnswer(
    SymptomDiaryQuestionDefinition Question,
    JsonElement Value,
    string SubmittedValueJson);

public sealed record RecordSymptomCheckInResult(
    EntityId CheckInId,
    EntityId EpisodeId,
    EntityId PackageVersionId,
    ClinicalPathwayCode Pathway,
    DateTimeOffset CreatedAt,
    SymptomDiaryPackageContent Package,
    IReadOnlyList<ValidatedSymptomDiaryAnswer> Answers,
    bool NewlyCreated);

public sealed class SymptomDiaryAnswerStructureValidator
{
    private const int MaximumSubmittedJsonLength = 65536;

    public IReadOnlyList<ValidatedSymptomDiaryAnswer> Validate(
        SymptomDiaryPackageDefinition package,
        IReadOnlyList<SymptomDiarySubmittedAnswer> submittedAnswers)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(submittedAnswers);
        var questions = package.Questions.ToDictionary(
            question => question.Code.Value,
            StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ValidatedSymptomDiaryAnswer>(submittedAnswers.Count);

        foreach (var submitted in submittedAnswers)
        {
            if (submitted is null ||
                submitted.HasUnsupportedFields ||
                string.IsNullOrWhiteSpace(submitted.QuestionCode) ||
                !seen.Add(submitted.QuestionCode) ||
                !questions.TryGetValue(submitted.QuestionCode, out var question))
            {
                throw new SymptomDiaryAnswerValidationException();
            }

            var value = submitted.Value.Clone();
            ValidateValue(question, value);
            var json = value.GetRawText();
            if (json.Length > MaximumSubmittedJsonLength)
            {
                throw new SymptomDiaryAnswerValidationException();
            }

            result.Add(new ValidatedSymptomDiaryAnswer(question, value, json));
        }

        if (package.Questions.Any(question => question.IsRequired &&
                !seen.Contains(question.Code.Value)))
        {
            throw new SymptomDiaryAnswerValidationException();
        }

        return result.OrderBy(answer => answer.Question.SourceOrder).ToArray();
    }

    private static void ValidateValue(
        SymptomDiaryQuestionDefinition question,
        JsonElement value)
    {
        JsonElement schema;
        try
        {
            using var document = JsonDocument.Parse(question.AnswerSchemaJson);
            schema = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new SymptomDiaryPackageIntegrityException(
                $"Question '{question.Code}' has malformed answer schema: {exception.Message}");
        }

        if (!schema.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
        {
            throw UnsupportedSchema(question);
        }

        switch (type.GetString())
        {
            case "string":
                ValidateString(question, schema, value);
                return;
            case "array":
                ValidateStringArray(question, schema, value);
                return;
            default:
                throw UnsupportedSchema(question);
        }
    }

    private static void ValidateString(
        SymptomDiaryQuestionDefinition question,
        JsonElement schema,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new SymptomDiaryAnswerValidationException();
        }

        var allowed = ReadEnum(question, schema, required: question.Options.Count > 0);
        if (allowed is not null && !allowed.Contains(value.GetString()!, StringComparer.Ordinal))
        {
            throw new SymptomDiaryAnswerValidationException();
        }

        EnsureOptionsAgree(question, allowed);
    }

    private static void ValidateStringArray(
        SymptomDiaryQuestionDefinition question,
        JsonElement schema,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array ||
            !schema.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Object ||
            !items.TryGetProperty("type", out var itemType) ||
            itemType.GetString() != "string" ||
            !schema.TryGetProperty("uniqueItems", out var uniqueItems) ||
            uniqueItems.ValueKind != JsonValueKind.True)
        {
            throw value.ValueKind == JsonValueKind.Array
                ? UnsupportedSchema(question)
                : new SymptomDiaryAnswerValidationException();
        }

        var allowed = ReadEnum(question, items, required: true)!;
        EnsureOptionsAgree(question, allowed);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !seen.Add(item.GetString()!) ||
                !allowed.Contains(item.GetString()!, StringComparer.Ordinal))
            {
                throw new SymptomDiaryAnswerValidationException();
            }
        }
    }

    private static IReadOnlyList<string>? ReadEnum(
        SymptomDiaryQuestionDefinition question,
        JsonElement schema,
        bool required)
    {
        if (!schema.TryGetProperty("enum", out var values))
        {
            return required ? throw UnsupportedSchema(question) : null;
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw UnsupportedSchema(question);
        }

        var result = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !unique.Add(value.GetString()!))
            {
                throw UnsupportedSchema(question);
            }

            result.Add(value.GetString()!);
        }

        return result;
    }

    private static void EnsureOptionsAgree(
        SymptomDiaryQuestionDefinition question,
        IReadOnlyList<string>? allowed)
    {
        var options = question.Options
            .OrderBy(option => option.SourceOrder)
            .Select(option => option.Value)
            .ToArray();
        if ((allowed is null && options.Length != 0) ||
            (allowed is not null && !allowed.SequenceEqual(options, StringComparer.Ordinal)))
        {
            throw UnsupportedSchema(question);
        }
    }

    private static SymptomDiaryPackageIntegrityException UnsupportedSchema(
        SymptomDiaryQuestionDefinition question) => new(
            $"Question '{question.Code}' has an unsupported or inconsistent answer schema.");
}

public static class SymptomCheckInRequestHashCalculator
{
    public static SymptomDiarySha256 Calculate(
        EntityId packageVersionId,
        IReadOnlyList<ValidatedSymptomDiaryAnswer> answers)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("packageVersionId", packageVersionId.Value.ToString("D"));
            writer.WritePropertyName("answers");
            writer.WriteStartArray();
            foreach (var answer in answers.OrderBy(value => value.Question.SourceOrder))
            {
                writer.WriteStartObject();
                writer.WriteString("questionCode", answer.Question.Code.Value);
                writer.WritePropertyName("value");
                answer.Value.WriteTo(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return SymptomDiarySha256.FromHash(
            Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant());
    }
}

public interface ISymptomCheckInTransaction : IAsyncDisposable
{
    Task BeginAsync(
        EntityId episodeId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SymptomCheckInEpisodeSource?> FindEpisodeAsync(
        EntityId episodeId,
        CancellationToken cancellationToken = default);

    Task<SymptomCheckIn?> FindExistingAsync(
        EntityId episodeId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SymptomDiaryPackageVersion?> FindPackageAsync(
        EntityId packageVersionId,
        CancellationToken cancellationToken = default);

    void Add(SymptomCheckIn checkIn);

    Task<SymptomCheckInSaveResult> SaveAsync(
        SymptomCheckIn checkIn,
        CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

public sealed record SymptomCheckInEpisodeSource(
    PreTriageEpisode Episode,
    QuestionnaireDefinitionVersion FrozenQuestionnaire)
{
    public EntityId PatientProfileId => Episode.PatientProfileId ??
        throw new InvalidOperationException("A check-in episode must have persisted ownership.");

    public ClinicalPathwayCode Pathway => FrozenQuestionnaire.Pathway;
}

public sealed record SymptomCheckInSaveResult(
    SymptomCheckIn CheckIn,
    bool NewlyCreated);

public interface ISymptomCheckInAuditLogger
{
    void Recorded(
        EntityId actorAccountId,
        EntityId episodeId,
        EntityId packageVersionId,
        EntityId checkInId,
        int answerCount,
        PatientAccessReason accessReason,
        bool newlyCreated,
        DateTimeOffset createdAt);

    void IdempotencyConflict(
        EntityId episodeId,
        EntityId packageVersionId,
        EntityId idempotencyKey,
        DateTimeOffset rejectedAt);
}

public sealed class SymptomDiaryAnswerValidationException : Exception;

public sealed class SymptomCheckInIdempotencyConflictException : Exception;
