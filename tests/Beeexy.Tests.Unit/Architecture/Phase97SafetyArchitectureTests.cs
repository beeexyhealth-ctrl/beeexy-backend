using System.Reflection;
using Beeexy.Api.Care;
using Beeexy.Application.Care;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Architecture;

[Trait("Category", "Phase97")]
[Trait("Category", "Phase97Architecture")]
public sealed class Phase97SafetyArchitectureTests
{
    private static readonly string[] ForbiddenSemanticNames =
    [
        "Diagnosis", "Probability", "Severity", "Risk", "Urgency", "Disposition",
        "Trend", "Delta", "Comparison", "WarningMatcher", "RedFlag", "Recommendation",
        "Medication", "Escalation", "Reminder", "Notification", "Scheduling", "Outbox",
        "AiProvider", "Fhir", "ClinicalHistory", "Adherence", "Compliance", "Overdue",
        "Cadence", "UpdateSymptomCheckIn", "DeleteSymptomCheckIn"
    ];

    [Fact]
    public void ThreeUseCasesHaveOnlyTheIntendedNeutralDependencyChain()
    {
        Assert.Equal(
            [
                typeof(AuthorizePatientAccess),
                typeof(ISymptomDiaryEpisodeReadRepository),
                typeof(ISymptomDiaryContentProvider)
            ],
            ConstructorDependencies<GetSymptomDiaryContent>());
        Assert.Equal(
            [
                typeof(IClock),
                typeof(CurrentAccountProfileResolver),
                typeof(AuthorizePatientAccess),
                typeof(ISymptomDiaryEpisodeReadRepository),
                typeof(ISymptomDiaryContentProvider),
                typeof(SymptomDiaryPackageValidator),
                typeof(SymptomDiaryAnswerStructureValidator),
                typeof(ISymptomCheckInTransaction),
                typeof(ISymptomCheckInAuditLogger)
            ],
            ConstructorDependencies<RecordSymptomCheckIn>());
        Assert.Equal(
            [
                typeof(AuthorizePatientAccess),
                typeof(ISymptomDiaryEpisodeReadRepository),
                typeof(ISymptomCheckInReadRepository),
                typeof(ISymptomDiaryExactContentBatchProvider),
                typeof(ISymptomDiaryHistoryCursorCodec),
                typeof(SymptomDiaryPackageValidator),
                typeof(ISymptomDiaryHistoryAuditLogger)
            ],
            ConstructorDependencies<ListSymptomCheckIns>());
    }

    [Fact]
    public void Phase9ProductionTypesExposeNoClinicalEvaluatorOrDeferredIntegration()
    {
        var productionTypes = new[]
            {
                typeof(SymptomCheckIn).Assembly,
                typeof(GetSymptomDiaryContent).Assembly,
                typeof(SymptomDiaryContentProvider).Assembly,
                typeof(SymptomDiaryEndpointExtensions).Assembly
            }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsPhase9Type)
            .ToArray();

        Assert.NotEmpty(productionTypes);
        Assert.DoesNotContain(productionTypes, type =>
            ContainsForbiddenSemantic(type.Name) ||
            type.GetInterfaces().Any(value =>
                value.FullName is "Microsoft.Extensions.Hosting.IHostedService"));
        Assert.DoesNotContain(
            productionTypes.SelectMany(type => type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly)),
            member => ContainsForbiddenSemantic(member.Name));
    }

    [Fact]
    public void WarningSignsRemainStandaloneStaticDisplayInformation()
    {
        Assert.Equal(
            ["Code", "DisplayText", "Id", "PackageVersionId", "SourceOrder"],
            typeof(SymptomWarningSign).GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name));
        Assert.DoesNotContain(
            typeof(SymptomWarningSign).GetProperties(),
            property => property.Name.Contains("Answer", StringComparison.OrdinalIgnoreCase) ||
                ContainsForbiddenSemantic(property.Name));
        Assert.DoesNotContain(
            typeof(SymptomCheckInAnswer).GetProperties(),
            property => property.PropertyType == typeof(SymptomWarningSign) ||
                property.Name.Contains("Warning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiarySurfaceIsAppendOnlyAndHasNoCadenceOrMutationContract()
    {
        Assert.Equal(
            "/api/v1/pre-triage/episodes/{episodeId:guid}/symptom-diary-content",
            SymptomDiaryEndpointExtensions.ContentRoute);
        Assert.Equal(
            "/api/v1/pre-triage/episodes/{episodeId:guid}/check-ins",
            SymptomDiaryEndpointExtensions.CheckInRoute);
        Assert.Equal(20, ListSymptomCheckIns.DefaultPageSize);
        Assert.Equal(100, ListSymptomCheckIns.MaximumPageSize);

        var mutableOperations = new[]
            {
                typeof(SymptomCheckIn),
                typeof(SymptomCheckInAnswer),
                typeof(SymptomDiaryPackageVersion),
                typeof(SymptomDiaryQuestion),
                typeof(SymptomDiaryQuestionOption),
                typeof(SymptomWarningSign)
            }
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.Name.StartsWith("Update", StringComparison.Ordinal) ||
                method.Name.StartsWith("Delete", StringComparison.Ordinal) ||
                method.Name.StartsWith("Remove", StringComparison.Ordinal) ||
                method.Name.StartsWith("Schedule", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(mutableOperations);
    }

    private static Type[] ConstructorDependencies<T>() =>
        typeof(T).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

    private static bool IsPhase9Type(Type type) =>
        type.Namespace is not null &&
        (type.Name.Contains("SymptomDiary", StringComparison.Ordinal) ||
         type.Name.Contains("SymptomCheckIn", StringComparison.Ordinal));

    private static bool ContainsForbiddenSemantic(string value) =>
        ForbiddenSemanticNames.Any(name =>
            value.Contains(name, StringComparison.OrdinalIgnoreCase));
}
