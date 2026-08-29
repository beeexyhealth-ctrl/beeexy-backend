using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.DirectoryServices;

public sealed class PublicDirectoryQueryBoundary(BeeexyDbContext dbContext)
{
    public IQueryable<Clinic> Clinics() => PublishedClinics(dbContext.Clinics);

    public IQueryable<Clinic> ClinicsAfter(EntityId clinicId) =>
        PublishedClinics(dbContext.Clinics.FromSqlInterpolated($"""
            SELECT clinic.*
            FROM directory.clinics AS clinic
            WHERE clinic.id > {clinicId.Value}
            """));

    public IQueryable<ClinicLocation> ClinicLocations() =>
        dbContext.ClinicLocations.AsNoTracking().Where(location =>
            location.IsPublished &&
            dbContext.Clinics.Any(clinic =>
                clinic.Id == location.ClinicId && clinic.IsPublished));

    public IQueryable<Doctor> Doctors() =>
        PublishedDoctors(dbContext.Doctors);

    public IQueryable<Doctor> DoctorsAfter(EntityId doctorId) =>
        PublishedDoctors(dbContext.Doctors.FromSqlInterpolated($"""
            SELECT doctor.*
            FROM directory.doctors AS doctor
            WHERE doctor.id > {doctorId.Value}
            """));

    public IQueryable<DoctorAffiliation> DoctorAffiliations() =>
        dbContext.DoctorAffiliations.AsNoTracking().Where(affiliation =>
            affiliation.IsPublished &&
            dbContext.Doctors.Any(doctor =>
                doctor.Id == affiliation.DoctorId && doctor.IsPublished) &&
            dbContext.Clinics.Any(clinic =>
                clinic.Id == affiliation.ClinicId && clinic.IsPublished) &&
            (!affiliation.ClinicLocationId.HasValue ||
                dbContext.ClinicLocations.Any(location =>
                    location.Id == affiliation.ClinicLocationId.Value &&
                    location.ClinicId == affiliation.ClinicId &&
                    location.IsPublished)));

    public IQueryable<DoctorCredential> DoctorCredentials() =>
        dbContext.DoctorCredentials.AsNoTracking().Where(credential =>
            credential.Status == DoctorCredentialStatus.Verified &&
            dbContext.Doctors.Any(doctor =>
                doctor.Id == credential.DoctorId && doctor.IsPublished));

    public IQueryable<DoctorSpecialty> DoctorSpecialties() =>
        dbContext.DoctorSpecialties.AsNoTracking().Where(relationship =>
            dbContext.Doctors.Any(doctor =>
                doctor.Id == relationship.DoctorId && doctor.IsPublished));

    public IQueryable<Specialty> Specialties() => dbContext.Specialties.AsNoTracking();

    public IQueryable<DoctorLanguage> DoctorLanguages() =>
        dbContext.DoctorLanguages.AsNoTracking().Where(relationship =>
            dbContext.Doctors.Any(doctor =>
                doctor.Id == relationship.DoctorId && doctor.IsPublished));

    public IQueryable<Language> Languages() => dbContext.Languages.AsNoTracking();

    public IQueryable<DoctorInsuranceParticipation> DoctorInsuranceParticipations() =>
        dbContext.DoctorInsuranceParticipations.AsNoTracking().Where(relationship =>
            dbContext.Doctors.Any(doctor =>
                doctor.Id == relationship.DoctorId && doctor.IsPublished));

    public IQueryable<InsurancePlan> InsurancePlans() =>
        dbContext.InsurancePlans.AsNoTracking();

    private static IQueryable<Clinic> PublishedClinics(IQueryable<Clinic> clinics) =>
        clinics.AsNoTracking().Where(value => value.IsPublished);

    private static IQueryable<Doctor> PublishedDoctors(IQueryable<Doctor> doctors) =>
        doctors.AsNoTracking().Where(value => value.IsPublished);
}
