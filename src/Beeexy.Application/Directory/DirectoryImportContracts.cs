using Beeexy.Domain.Directory;

namespace Beeexy.Application.Directory;

public sealed class DirectoryImportPackage
{
    private DirectoryImportPackage(
        DirectoryCode packageCode,
        DirectoryCode version,
        Clinic[] clinics,
        ClinicLocation[] clinicLocations,
        Doctor[] doctors,
        DoctorAffiliation[] doctorAffiliations,
        DoctorCredential[] doctorCredentials,
        Specialty[] specialties,
        DoctorSpecialty[] doctorSpecialties,
        Language[] languages,
        DoctorLanguage[] doctorLanguages,
        InsurancePlan[] insurancePlans,
        DoctorInsuranceParticipation[] doctorInsuranceParticipations)
    {
        PackageCode = packageCode;
        Version = version;
        Clinics = Array.AsReadOnly(clinics);
        ClinicLocations = Array.AsReadOnly(clinicLocations);
        Doctors = Array.AsReadOnly(doctors);
        DoctorAffiliations = Array.AsReadOnly(doctorAffiliations);
        DoctorCredentials = Array.AsReadOnly(doctorCredentials);
        Specialties = Array.AsReadOnly(specialties);
        DoctorSpecialties = Array.AsReadOnly(doctorSpecialties);
        Languages = Array.AsReadOnly(languages);
        DoctorLanguages = Array.AsReadOnly(doctorLanguages);
        InsurancePlans = Array.AsReadOnly(insurancePlans);
        DoctorInsuranceParticipations = Array.AsReadOnly(doctorInsuranceParticipations);
        ContentHash = DirectoryImportIntegrity.Calculate(this);
    }

    public DirectoryCode PackageCode { get; }

    public DirectoryCode Version { get; }

    public string ContentHash { get; }

    public IReadOnlyList<Clinic> Clinics { get; }

    public IReadOnlyList<ClinicLocation> ClinicLocations { get; }

    public IReadOnlyList<Doctor> Doctors { get; }

    public IReadOnlyList<DoctorAffiliation> DoctorAffiliations { get; }

    public IReadOnlyList<DoctorCredential> DoctorCredentials { get; }

    public IReadOnlyList<Specialty> Specialties { get; }

    public IReadOnlyList<DoctorSpecialty> DoctorSpecialties { get; }

    public IReadOnlyList<Language> Languages { get; }

    public IReadOnlyList<DoctorLanguage> DoctorLanguages { get; }

    public IReadOnlyList<InsurancePlan> InsurancePlans { get; }

    public IReadOnlyList<DoctorInsuranceParticipation> DoctorInsuranceParticipations { get; }

    public static DirectoryImportPackage Create(
        DirectoryCode packageCode,
        DirectoryCode version,
        IEnumerable<Clinic> clinics,
        IEnumerable<ClinicLocation> clinicLocations,
        IEnumerable<Doctor> doctors,
        IEnumerable<DoctorAffiliation> doctorAffiliations,
        IEnumerable<DoctorCredential> doctorCredentials,
        IEnumerable<Specialty> specialties,
        IEnumerable<DoctorSpecialty> doctorSpecialties,
        IEnumerable<Language> languages,
        IEnumerable<DoctorLanguage> doctorLanguages,
        IEnumerable<InsurancePlan> insurancePlans,
        IEnumerable<DoctorInsuranceParticipation> doctorInsuranceParticipations)
    {
        ArgumentNullException.ThrowIfNull(packageCode);
        ArgumentNullException.ThrowIfNull(version);
        return new DirectoryImportPackage(
            packageCode,
            version,
            ToArray(clinics, nameof(clinics)),
            ToArray(clinicLocations, nameof(clinicLocations)),
            ToArray(doctors, nameof(doctors)),
            ToArray(doctorAffiliations, nameof(doctorAffiliations)),
            ToArray(doctorCredentials, nameof(doctorCredentials)),
            ToArray(specialties, nameof(specialties)),
            ToArray(doctorSpecialties, nameof(doctorSpecialties)),
            ToArray(languages, nameof(languages)),
            ToArray(doctorLanguages, nameof(doctorLanguages)),
            ToArray(insurancePlans, nameof(insurancePlans)),
            ToArray(doctorInsuranceParticipations, nameof(doctorInsuranceParticipations)));
    }

    private static T[] ToArray<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var result = values.ToArray();
        if (result.Any(value => value is null))
        {
            throw new ArgumentException("Package collections cannot contain null values.", parameterName);
        }

        return result;
    }
}

public enum DirectoryImportOutcome
{
    Imported,
    AlreadyImported
}

public sealed record DirectoryImportResult(
    DirectoryImportOutcome Outcome,
    DirectoryCode PackageCode,
    DirectoryCode Version,
    string ContentHash);

public interface IDirectoryImporter
{
    Task<DirectoryImportResult> ImportAsync(
        DirectoryImportPackage package,
        CancellationToken cancellationToken = default);
}

public sealed class DirectoryImportValidationException(string message) : Exception(message);

public sealed class DirectoryImportConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
