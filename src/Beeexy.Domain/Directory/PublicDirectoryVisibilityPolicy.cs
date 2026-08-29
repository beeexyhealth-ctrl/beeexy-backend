namespace Beeexy.Domain.Directory;

public static class PublicDirectoryVisibilityPolicy
{
    public static bool IsClinicEligible(Clinic clinic)
    {
        ArgumentNullException.ThrowIfNull(clinic);
        return clinic.IsPublished;
    }

    public static bool IsDoctorEligible(Doctor doctor)
    {
        ArgumentNullException.ThrowIfNull(doctor);
        return doctor.IsPublished;
    }

    public static bool IsLocationEligible(ClinicLocation location, Clinic clinic)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clinic);
        return clinic.IsPublished &&
            location.IsPublished &&
            location.ClinicId == clinic.Id;
    }

    public static bool IsAffiliationEligible(
        DoctorAffiliation affiliation,
        Doctor doctor,
        Clinic clinic,
        ClinicLocation? location)
    {
        ArgumentNullException.ThrowIfNull(affiliation);
        ArgumentNullException.ThrowIfNull(doctor);
        ArgumentNullException.ThrowIfNull(clinic);

        var locationMatches = affiliation.ClinicLocationId.HasValue
            ? location is not null &&
                location.Id == affiliation.ClinicLocationId.Value &&
                location.ClinicId == clinic.Id &&
                location.IsPublished
            : location is null;

        return affiliation.IsPublished &&
            doctor.IsPublished &&
            clinic.IsPublished &&
            affiliation.DoctorId == doctor.Id &&
            affiliation.ClinicId == clinic.Id &&
            locationMatches;
    }

    public static bool IsCredentialEligible(DoctorCredential credential, Doctor doctor)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(doctor);
        return doctor.IsPublished &&
            credential.DoctorId == doctor.Id &&
            credential.Status == DoctorCredentialStatus.Verified;
    }
}
