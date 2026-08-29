using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Tests.Unit.Domain;

public sealed class DirectoryDomainTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 28, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValueObjects_TrimAndRetainNeutralDirectoryValues()
    {
        Assert.Equal("clinic-001", DirectoryCode.Create(" clinic-001 ").Value);
        Assert.Equal("Demo directory entry", DirectoryName.Create(" Demo directory entry ").Value);
        Assert.Equal("America/Lima", IanaTimeZone.Create("America/Lima").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has whitespace")]
    public void DirectoryCode_RejectsInvalidValues(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => DirectoryCode.Create(value!));
    }

    [Fact]
    public void IanaTimeZone_RejectsUnknownOrMissingValues()
    {
        Assert.Throws<ArgumentException>(() => IanaTimeZone.Create(""));
        Assert.Throws<ArgumentException>(() => IanaTimeZone.Create("Not/A_Timezone"));
    }

    [Fact]
    public void ClinicDoctorAndLocation_PreserveIdentityAndPublicationState()
    {
        var clinic = Clinic.Create(
            DirectoryCode.Create("clinic-001"),
            DirectoryName.Create("Demo clinic record"),
            true,
            CreatedAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create("doctor-001"),
            DirectoryName.Create("Demo doctor record"),
            false,
            CreatedAt);
        var location = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Demo location record"),
            "Lima",
            "Lima",
            "Peru",
            IanaTimeZone.Create("America/Lima"),
            true,
            CreatedAt);

        Assert.NotEqual(Guid.Empty, clinic.Id.Value);
        Assert.NotEqual(Guid.Empty, doctor.Id.Value);
        Assert.Equal(clinic.Id, location.ClinicId);
        Assert.True(clinic.IsPublished);
        Assert.False(doctor.IsPublished);
        Assert.True(location.IsPublished);
        Assert.Equal(CreatedAt, location.CreatedAt);
    }

    [Fact]
    public void ClinicLocation_RejectsMissingRelationshipLocationDataAndNonUtcTimestamp()
    {
        Assert.Throws<ArgumentException>(() => ClinicLocation.Create(
            default,
            DirectoryName.Create("Location"),
            "Locality",
            "Area",
            "Country",
            IanaTimeZone.Create("America/Lima"),
            false,
            CreatedAt));
        Assert.Throws<ArgumentException>(() => ClinicLocation.Create(
            EntityId.New(),
            DirectoryName.Create("Location"),
            " ",
            "Area",
            "Country",
            IanaTimeZone.Create("America/Lima"),
            false,
            CreatedAt));
        Assert.Throws<ArgumentException>(() => ClinicLocation.Create(
            EntityId.New(),
            DirectoryName.Create("Location"),
            "Locality",
            "Area",
            "Country",
            IanaTimeZone.Create("America/Lima"),
            false,
            CreatedAt.ToOffset(TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void CredentialVocabulary_IsExactlyApprovedAndRejectsUndefinedValue()
    {
        Assert.Equal(
            ["Submitted", "PendingVerification", "Verified", "Rejected"],
            Enum.GetNames<DoctorCredentialStatus>());

        Assert.Throws<ArgumentOutOfRangeException>(() => DoctorCredential.Create(
            EntityId.New(),
            DirectoryName.Create("Credential claim"),
            (DoctorCredentialStatus)99,
            CreatedAt));
    }

    [Theory]
    [InlineData(DoctorCredentialStatus.Submitted)]
    [InlineData(DoctorCredentialStatus.PendingVerification)]
    [InlineData(DoctorCredentialStatus.Verified)]
    [InlineData(DoctorCredentialStatus.Rejected)]
    public void Credential_AllApprovedStatesCanBeRepresented(DoctorCredentialStatus status)
    {
        var credential = DoctorCredential.Create(
            EntityId.New(),
            DirectoryName.Create("Demo dataset claim"),
            status,
            CreatedAt);

        Assert.Equal(status, credential.Status);
    }

    [Fact]
    public void Affiliation_RequiresDoctorAndClinicAndRejectsEmptyLocation()
    {
        Assert.Throws<ArgumentException>(() => DoctorAffiliation.Create(
            default,
            EntityId.New(),
            null,
            false,
            CreatedAt));
        Assert.Throws<ArgumentException>(() => DoctorAffiliation.Create(
            EntityId.New(),
            default,
            null,
            false,
            CreatedAt));
        Assert.Throws<ArgumentException>(() => DoctorAffiliation.Create(
            EntityId.New(),
            EntityId.New(),
            default(EntityId),
            false,
            CreatedAt));
    }

    [Fact]
    public void NormalizedCatalogRelationships_ConstructWithStableUuidReferences()
    {
        var doctorId = EntityId.New();
        var specialty = Specialty.Create(
            DirectoryCode.Create("specialty-001"),
            DirectoryName.Create("Demo specialty"),
            CreatedAt);
        var language = Language.Create(
            DirectoryCode.Create("language-001"),
            DirectoryName.Create("Demo language"),
            CreatedAt);
        var insurance = InsurancePlan.Create(
            DirectoryCode.Create("plan-001"),
            DirectoryName.Create("Demo insurance plan"),
            CreatedAt);

        var doctorSpecialty = DoctorSpecialty.Create(doctorId, specialty.Id, CreatedAt);
        var doctorLanguage = DoctorLanguage.Create(doctorId, language.Id, CreatedAt);
        var participation = DoctorInsuranceParticipation.Create(
            doctorId,
            insurance.Id,
            CreatedAt);

        Assert.Equal(specialty.Id, doctorSpecialty.SpecialtyId);
        Assert.Equal(language.Id, doctorLanguage.LanguageId);
        Assert.Equal(insurance.Id, participation.InsurancePlanId);
    }

    [Fact]
    public void MatchRuleVersion_IsAStandaloneVersionBoundaryWithoutDoctorOrScoringFields()
    {
        var version = DoctorMatchRuleVersion.Create(
            DirectoryCode.Create("demo-rules-v1"),
            CreatedAt);
        var propertyNames = typeof(DoctorMatchRuleVersion)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(["CreatedAt", "Id", "Version"], propertyNames);
        Assert.Equal("demo-rules-v1", version.Version.Value);
        Assert.DoesNotContain(propertyNames, name => name.Contains("Doctor", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Factor", StringComparison.Ordinal) ||
            name.Contains("Weight", StringComparison.Ordinal) ||
            name.Contains("Score", StringComparison.Ordinal));
    }
}
