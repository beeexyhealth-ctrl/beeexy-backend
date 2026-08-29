using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.DirectoryServices;

namespace Beeexy.Tests.Unit.DirectoryImport;

public sealed class DemoDirectoryPackageTests
{
    [Fact]
    public void ProductPackage_IsDeterministicVersionedAndContainsRequiredVariation()
    {
        var first = ProductApprovedSyntheticDirectory.Create();
        var second = ProductApprovedSyntheticDirectory.Create();

        Assert.Equal(ProductApprovedSyntheticDirectory.PackageCode, first.PackageCode.Value);
        Assert.Equal(ProductApprovedSyntheticDirectory.Version, first.Version.Value);
        Assert.Equal(ProductApprovedSyntheticDirectory.ExpectedContentHash, first.ContentHash);
        Assert.Equal(64, first.ContentHash.Length);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(
            first.Clinics.Select(value => value.Id),
            second.Clinics.Select(value => value.Id));
        Assert.True(first.Clinics.Count >= 2);
        Assert.True(first.Doctors.Count >= 2);
        Assert.True(first.Specialties.Count >= 2);
        Assert.True(first.Languages.Count >= 2);
        Assert.True(first.InsurancePlans.Count >= 2);
        Assert.Contains(first.Clinics, value => value.IsPublished);
        Assert.Contains(first.Clinics, value => !value.IsPublished);
        Assert.Contains(first.Doctors, value => value.IsPublished);
        Assert.Contains(first.Doctors, value => !value.IsPublished);
        Assert.Equal(
            Enum.GetValues<DoctorCredentialStatus>().Order(),
            first.DoctorCredentials.Select(value => value.Status).Distinct().Order());
        Assert.All(first.Clinics, value => Assert.StartsWith("demo-", value.Code.Value));
        Assert.All(first.Doctors, value => Assert.Contains("Synthetic Demo", value.DisplayName.Value));
    }

    [Fact]
    public void PackageHash_ChangesWhenContentChangesWithoutChangingVersion()
    {
        var package = ProductApprovedSyntheticDirectory.Create();
        var changedClinic = Clinic.Create(
            package.Clinics[0].Code,
            DirectoryName.Create("Synthetic Demo Clinic Changed Content"),
            package.Clinics[0].IsPublished,
            package.Clinics[0].CreatedAt,
            package.Clinics[0].Id);
        var changed = Copy(package, clinics: [changedClinic, .. package.Clinics.Skip(1)]);

        Assert.Equal(package.PackageCode, changed.PackageCode);
        Assert.Equal(package.Version, changed.Version);
        Assert.NotEqual(package.ContentHash, changed.ContentHash);
    }

    [Fact]
    public void Validator_RejectsInvalidPackageReference()
    {
        var package = ProductApprovedSyntheticDirectory.Create();
        var affiliation = package.DoctorAffiliations[0];
        var invalid = DoctorAffiliation.Create(
            EntityId.New(),
            affiliation.ClinicId,
            affiliation.ClinicLocationId,
            affiliation.IsPublished,
            affiliation.CreatedAt,
            affiliation.Id);
        var packageWithInvalidReference = Copy(
            package,
            affiliations: [invalid, .. package.DoctorAffiliations.Skip(1)]);

        Assert.Throws<DirectoryImportValidationException>(() =>
            new DirectoryImportPackageValidator().Validate(packageWithInvalidReference));
    }

    [Fact]
    public void VisibilityPolicy_RequiresPublishedParentsAndRelationship()
    {
        var package = ProductApprovedSyntheticDirectory.Create();
        var publicAffiliation = package.DoctorAffiliations.Single(value =>
            value.Id.Value == Guid.Parse("71020000-0000-4300-8000-000000000031"));
        var doctor = package.Doctors.Single(value => value.Id == publicAffiliation.DoctorId);
        var clinic = package.Clinics.Single(value => value.Id == publicAffiliation.ClinicId);
        var location = package.ClinicLocations.Single(value =>
            value.Id == publicAffiliation.ClinicLocationId);
        var unpublishedClinicAffiliation = package.DoctorAffiliations.Single(value =>
            value.Id.Value == Guid.Parse("71020000-0000-4300-8000-000000000034"));
        var unpublishedClinic = package.Clinics.Single(value =>
            value.Id == unpublishedClinicAffiliation.ClinicId);
        var coral = package.Doctors.Single(value =>
            value.Id == unpublishedClinicAffiliation.DoctorId);
        var archiveLocation = package.ClinicLocations.Single(value =>
            value.Id == unpublishedClinicAffiliation.ClinicLocationId);

        Assert.True(PublicDirectoryVisibilityPolicy.IsClinicEligible(clinic));
        Assert.True(PublicDirectoryVisibilityPolicy.IsDoctorEligible(doctor));
        Assert.True(PublicDirectoryVisibilityPolicy.IsLocationEligible(location, clinic));
        Assert.True(PublicDirectoryVisibilityPolicy.IsAffiliationEligible(
            publicAffiliation,
            doctor,
            clinic,
            location));
        Assert.False(PublicDirectoryVisibilityPolicy.IsAffiliationEligible(
            unpublishedClinicAffiliation,
            coral,
            unpublishedClinic,
            archiveLocation));
    }

    [Theory]
    [InlineData(DoctorCredentialStatus.Submitted, false)]
    [InlineData(DoctorCredentialStatus.PendingVerification, false)]
    [InlineData(DoctorCredentialStatus.Verified, true)]
    [InlineData(DoctorCredentialStatus.Rejected, false)]
    public void VisibilityPolicy_OnlyAllowsVerifiedClaimsForPublishedDoctor(
        DoctorCredentialStatus status,
        bool expected)
    {
        var doctor = Doctor.Create(
            DirectoryCode.Create("demo-doctor-policy"),
            DirectoryName.Create("Synthetic Demo Doctor Policy"),
            true,
            DateTimeOffset.UnixEpoch);
        var credential = DoctorCredential.Create(
            doctor.Id,
            DirectoryName.Create("Synthetic Demo Dataset Claim"),
            status,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(
            expected,
            PublicDirectoryVisibilityPolicy.IsCredentialEligible(credential, doctor));
    }

    private static DirectoryImportPackage Copy(
        DirectoryImportPackage source,
        IEnumerable<Clinic>? clinics = null,
        IEnumerable<DoctorAffiliation>? affiliations = null) =>
        DirectoryImportPackage.Create(
            source.PackageCode,
            source.Version,
            clinics ?? source.Clinics,
            source.ClinicLocations,
            source.Doctors,
            affiliations ?? source.DoctorAffiliations,
            source.DoctorCredentials,
            source.Specialties,
            source.DoctorSpecialties,
            source.Languages,
            source.DoctorLanguages,
            source.InsurancePlans,
            source.DoctorInsuranceParticipations);
}
