using Beeexy.Domain.Common;

namespace Beeexy.Application.Directory;

public sealed class DirectoryImportPackageValidator
{
    public void Validate(DirectoryImportPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        RequireContent(package.Clinics, "clinics");
        RequireContent(package.ClinicLocations, "clinic locations");
        RequireContent(package.Doctors, "doctors");
        RequireContent(package.DoctorAffiliations, "doctor affiliations");
        RequireContent(package.DoctorCredentials, "doctor credentials");
        RequireContent(package.Specialties, "specialties");
        RequireContent(package.DoctorSpecialties, "doctor specialties");
        RequireContent(package.Languages, "languages");
        RequireContent(package.DoctorLanguages, "doctor languages");
        RequireContent(package.InsurancePlans, "insurance plans");
        RequireContent(package.DoctorInsuranceParticipations, "insurance participations");

        EnsureUniqueIds(package);
        EnsureUniqueCodes(package);
        EnsureReferences(package);

        if (!string.Equals(
            package.ContentHash,
            DirectoryImportIntegrity.Calculate(package),
            StringComparison.Ordinal))
        {
            throw new DirectoryImportValidationException(
                "Directory package content does not match its immutable content hash.");
        }
    }

    private static void EnsureUniqueIds(DirectoryImportPackage package)
    {
        var ids = package.Clinics.Select(value => value.Id)
            .Concat(package.ClinicLocations.Select(value => value.Id))
            .Concat(package.Doctors.Select(value => value.Id))
            .Concat(package.DoctorAffiliations.Select(value => value.Id))
            .Concat(package.DoctorCredentials.Select(value => value.Id))
            .Concat(package.Specialties.Select(value => value.Id))
            .Concat(package.DoctorSpecialties.Select(value => value.Id))
            .Concat(package.Languages.Select(value => value.Id))
            .Concat(package.DoctorLanguages.Select(value => value.Id))
            .Concat(package.InsurancePlans.Select(value => value.Id))
            .Concat(package.DoctorInsuranceParticipations.Select(value => value.Id))
            .ToArray();
        if (ids.Distinct().Count() != ids.Length)
        {
            throw new DirectoryImportValidationException(
                "Directory package identifiers must be unique across the package.");
        }
    }

    private static void EnsureUniqueCodes(DirectoryImportPackage package)
    {
        EnsureUnique(package.Clinics.Select(value => value.Code.Value), "clinic codes");
        EnsureUnique(package.Doctors.Select(value => value.Code.Value), "doctor codes");
        EnsureUnique(package.Specialties.Select(value => value.Code.Value), "specialty codes");
        EnsureUnique(package.Languages.Select(value => value.Code.Value), "language codes");
        EnsureUnique(package.InsurancePlans.Select(value => value.Code.Value), "insurance codes");
    }

    private static void EnsureReferences(DirectoryImportPackage package)
    {
        var clinics = package.Clinics.ToDictionary(value => value.Id);
        var locations = package.ClinicLocations.ToDictionary(value => value.Id);
        var doctors = package.Doctors.Select(value => value.Id).ToHashSet();
        var specialties = package.Specialties.Select(value => value.Id).ToHashSet();
        var languages = package.Languages.Select(value => value.Id).ToHashSet();
        var insurancePlans = package.InsurancePlans.Select(value => value.Id).ToHashSet();

        if (locations.Values.Any(location => !clinics.ContainsKey(location.ClinicId)))
        {
            throw InvalidReference("clinic location");
        }

        foreach (var affiliation in package.DoctorAffiliations)
        {
            if (!doctors.Contains(affiliation.DoctorId) ||
                !clinics.ContainsKey(affiliation.ClinicId) ||
                affiliation.ClinicLocationId.HasValue &&
                (!locations.TryGetValue(affiliation.ClinicLocationId.Value, out var location) ||
                    location.ClinicId != affiliation.ClinicId))
            {
                throw InvalidReference("doctor affiliation");
            }
        }

        if (package.DoctorCredentials.Any(value => !doctors.Contains(value.DoctorId)) ||
            package.DoctorSpecialties.Any(value =>
                !doctors.Contains(value.DoctorId) || !specialties.Contains(value.SpecialtyId)) ||
            package.DoctorLanguages.Any(value =>
                !doctors.Contains(value.DoctorId) || !languages.Contains(value.LanguageId)) ||
            package.DoctorInsuranceParticipations.Any(value =>
                !doctors.Contains(value.DoctorId) ||
                !insurancePlans.Contains(value.InsurancePlanId)))
        {
            throw InvalidReference("normalized doctor relationship");
        }
    }

    private static DirectoryImportValidationException InvalidReference(string category) =>
        new($"The directory package contains an invalid {category} reference.");

    private static void RequireContent<T>(IReadOnlyCollection<T> values, string category)
    {
        if (values.Count == 0)
        {
            throw new DirectoryImportValidationException(
                $"The directory package must contain {category}.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string category)
    {
        var candidates = values.ToArray();
        if (candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Length)
        {
            throw new DirectoryImportValidationException(
                $"The directory package contains duplicate {category}.");
        }
    }
}
