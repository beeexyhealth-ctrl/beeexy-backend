using Beeexy.Domain.Interoperability;

namespace Beeexy.Application.Interoperability;

public enum FhirValidationBlocker
{
    ReleaseNeutralArtifact = 1,
    FhirReleaseUnresolved = 2,
    RequiredProfilesUnresolved = 3,
    ResourceIdentityAndReferencesUnresolved = 4,
    QuestionnaireLinkIdUnresolved = 5,
    QuestionnaireAnswerValueTranslationUnresolved = 6,
    MandatoryRiskAssessmentContentUnavailable = 7,
    RequiredResourceSetIncomplete = 8,
    NoApprovedValidationSpecification = 9
}

public enum FhirValidationBlockerCategory
{
    ReleaseNeutralRepresentation = 1,
    SpecificationUnresolved = 2,
    RequiredResourceContentUnavailable = 3
}

public sealed record FhirValidationSpecification
{
    public const string OfficialFhirJsonMediaType = "application/fhir+json";

    private FhirValidationSpecification(
        string fhirRelease,
        string mappingVersion,
        FhirProfileResolution profileResolution)
    {
        FhirRelease = fhirRelease;
        MappingVersion = mappingVersion;
        ProfileResolution = profileResolution;
    }

    public string FhirRelease { get; }

    public string MappingVersion { get; }

    public FhirProfileResolution ProfileResolution { get; }

    public string ArtifactMediaType => OfficialFhirJsonMediaType;

    public static FhirValidationSpecification Create(
        string fhirRelease,
        string mappingVersion,
        FhirProfileResolution profileResolution)
    {
        ArgumentNullException.ThrowIfNull(profileResolution);
        if (profileResolution.Status == FhirProfileResolutionStatus.Unresolved)
        {
            throw new ArgumentException(
                "Profile applicability must be resolved for validation.",
                nameof(profileResolution));
        }

        var identity = FhirMappingSpecificationIdentity.Create(
            mappingVersion,
            fhirRelease,
            profileResolution);
        return new FhirValidationSpecification(
            identity.FhirRelease!,
            identity.MappingVersion,
            identity.ProfileResolution);
    }

    public bool Matches(FhirExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        return string.Equals(FhirRelease, export.FhirVersion, StringComparison.Ordinal) &&
            string.Equals(MappingVersion, export.MappingVersion, StringComparison.Ordinal) &&
            ProfileResolution.Status switch
            {
                FhirProfileResolutionStatus.NotApplicable =>
                    export.ProfileCanonical is null && export.ProfileVersion is null,
                FhirProfileResolutionStatus.Specified =>
                    string.Equals(
                        ProfileResolution.Canonical,
                        export.ProfileCanonical,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        ProfileResolution.Version,
                        export.ProfileVersion,
                        StringComparison.Ordinal),
                _ => false
            };
    }
}

public sealed record FhirValidationEligibility
{
    private FhirValidationEligibility(
        FhirValidationSpecification? specification,
        IReadOnlyList<FhirValidationBlocker> blockers)
    {
        Specification = specification;
        Blockers = blockers;
    }

    public bool IsEligible => Specification is not null;

    public FhirValidationSpecification? Specification { get; }

    public IReadOnlyList<FhirValidationBlocker> Blockers { get; }

    public IReadOnlyList<FhirValidationBlockerCategory> BlockerCategories => Blockers
        .Select(Category)
        .Distinct()
        .OrderBy(category => category)
        .ToArray();

    public static FhirValidationEligibility Eligible(
        FhirValidationSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return new FhirValidationEligibility(specification, []);
    }

    public static FhirValidationEligibility Blocked(
        IEnumerable<FhirValidationBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        var values = blockers.Distinct().OrderBy(blocker => blocker).ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException(
                "Blocked validation eligibility requires at least one blocker.",
                nameof(blockers));
        }

        return new FhirValidationEligibility(null, Array.AsReadOnly(values));
    }

    private static FhirValidationBlockerCategory Category(
        FhirValidationBlocker blocker) => blocker switch
        {
            FhirValidationBlocker.ReleaseNeutralArtifact =>
                FhirValidationBlockerCategory.ReleaseNeutralRepresentation,
            FhirValidationBlocker.MandatoryRiskAssessmentContentUnavailable or
                FhirValidationBlocker.RequiredResourceSetIncomplete =>
                FhirValidationBlockerCategory.RequiredResourceContentUnavailable,
            FhirValidationBlocker.FhirReleaseUnresolved or
                FhirValidationBlocker.RequiredProfilesUnresolved or
                FhirValidationBlocker.ResourceIdentityAndReferencesUnresolved or
                FhirValidationBlocker.QuestionnaireLinkIdUnresolved or
                FhirValidationBlocker.QuestionnaireAnswerValueTranslationUnresolved or
                FhirValidationBlocker.NoApprovedValidationSpecification =>
                FhirValidationBlockerCategory.SpecificationUnresolved,
            _ => throw new ArgumentOutOfRangeException(nameof(blocker))
        };
}

public interface IFhirValidationPrerequisiteEvaluator
{
    FhirValidationEligibility Evaluate(FhirExport export);
}

public sealed class CurrentFhirValidationPrerequisiteEvaluator
    : IFhirValidationPrerequisiteEvaluator
{
    private static readonly FhirValidationEligibility CurrentSnapshotBlocked =
        FhirValidationEligibility.Blocked(
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
        ]);

    public FhirValidationEligibility Evaluate(FhirExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        if (string.Equals(
            export.FhirVersion,
            FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
            StringComparison.Ordinal))
        {
            return CurrentSnapshotBlocked;
        }

        return FhirValidationEligibility.Blocked(
            [FhirValidationBlocker.NoApprovedValidationSpecification]);
    }
}
