using Beeexy.Application.Care;
using Beeexy.Domain.Care;

namespace Beeexy.Infrastructure.Care;

internal static class SymptomDiaryPackageMapper
{
    public static SymptomDiaryPackageVersion ToEntity(
        SymptomDiaryPackageDefinition definition,
        SymptomDiarySha256 contentHash)
    {
        return SymptomDiaryPackageVersion.Import(
            definition.PackageCode,
            definition.PackageVersion,
            definition.QuestionSetCode,
            definition.QuestionSetVersion,
            definition.SymptomInformationCode,
            definition.SymptomInformationVersion,
            definition.Pathway,
            contentHash,
            definition.ContentStatus,
            definition.ImportedAt,
            definition.ApprovedAt,
            definition.ActivatedAt,
            definition.SourceReference,
            definition.InformationalHeading,
            definition.InformationalBody,
            definition.Questions.Select(question => new SymptomDiaryQuestionInput(
                question.Code,
                question.PromptText,
                question.SourceOrder,
                question.AnswerSchemaJson,
                question.IsRequired,
                question.Options.Select(option => new SymptomDiaryQuestionOptionInput(
                    option.Code,
                    option.Value,
                    option.DisplayText,
                    option.SourceOrder)).ToArray())).ToArray(),
            definition.WarningSigns.Select(warningSign => new SymptomWarningSignInput(
                warningSign.Code,
                warningSign.DisplayText,
                warningSign.SourceOrder)).ToArray());
    }

    public static SymptomDiaryPackageContent ToContent(SymptomDiaryPackageVersion entity)
    {
        var definition = new SymptomDiaryPackageDefinition(
            entity.PackageCode,
            entity.PackageVersion,
            entity.QuestionSetCode,
            entity.QuestionSetVersion,
            entity.SymptomInformationCode,
            entity.SymptomInformationVersion,
            entity.Pathway,
            entity.ContentStatus,
            entity.SourceReference ?? string.Empty,
            entity.ImportedAt,
            entity.ApprovedAt,
            entity.ActivatedAt,
            entity.InformationalHeading,
            entity.InformationalBody,
            entity.Questions.OrderBy(question => question.SourceOrder)
                .Select(question => new SymptomDiaryQuestionDefinition(
                    question.Code,
                    question.PromptText,
                    question.SourceOrder,
                    question.AnswerSchemaJson,
                    question.IsRequired,
                    question.Options.OrderBy(option => option.SourceOrder)
                        .Select(option => new SymptomDiaryQuestionOptionDefinition(
                            option.Code,
                            option.Value,
                            option.DisplayText,
                            option.SourceOrder)).ToArray())).ToArray(),
            entity.WarningSigns.OrderBy(warningSign => warningSign.SourceOrder)
                .Select(warningSign => new SymptomWarningSignDefinition(
                    warningSign.Code,
                    warningSign.DisplayText,
                    warningSign.SourceOrder)).ToArray(),
            entity.CanonicalContentHash);
        return new SymptomDiaryPackageContent(
            entity.Id,
            entity.CanonicalContentHash,
            definition);
    }
}
