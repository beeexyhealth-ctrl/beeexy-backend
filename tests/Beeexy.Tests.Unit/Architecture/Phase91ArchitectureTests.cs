using Beeexy.Api.Middleware;
using Beeexy.Application.Patients;
using Beeexy.Domain.Care;
using Beeexy.Infrastructure.Persistence;

namespace Beeexy.Tests.Unit.Architecture;

public sealed class Phase91ArchitectureTests
{
    [Fact]
    [Trait("Category", "Phase91")]
    public void Phase91_AddsOnlyTheSixApprovedPersistedDomainConcepts()
    {
        var careTypes = typeof(SymptomCheckIn).Assembly.GetTypes()
            .Where(type => type.Namespace == "Beeexy.Domain.Care" && type.IsClass)
            .Select(type => type.Name)
            .ToArray();

        Assert.Contains(nameof(SymptomDiaryPackageVersion), careTypes);
        Assert.Contains(nameof(SymptomDiaryQuestion), careTypes);
        Assert.Contains(nameof(SymptomDiaryQuestionOption), careTypes);
        Assert.Contains(nameof(SymptomWarningSign), careTypes);
        Assert.Contains(nameof(SymptomCheckIn), careTypes);
        Assert.Contains(nameof(SymptomCheckInAnswer), careTypes);
        Assert.DoesNotContain("FollowUpAssessment", careTypes);
        Assert.DoesNotContain("CareGuideTemplateVersion", careTypes);
        Assert.DoesNotContain("CareGuideSnapshot", careTypes);
        Assert.DoesNotContain("ReminderIntent", careTypes);
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Phase91_IntroducesNoApplicationUseCaseOrApiEndpointType()
    {
        var applicationTypes = typeof(AuthorizePatientAccess).Assembly.GetTypes();
        var apiTypes = typeof(CorrelationIdMiddleware).Assembly.GetTypes();

        Assert.DoesNotContain(applicationTypes, type =>
            type.Name.Contains("SymptomDiary", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("SymptomCheckIn", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(apiTypes, type =>
            type.Name.Contains("SymptomDiary", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("SymptomCheckIn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Phase91Persistence_HasNoProductServiceOrExternalIntegrationType()
    {
        var infrastructureTypes = typeof(BeeexyDbContext).Assembly.GetTypes()
            .Where(type => type.Namespace?.Contains("Care", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(infrastructureTypes);
    }
}
