using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.DirectoryServices;

public sealed class PublicDirectoryQueryBoundary(BeeexyDbContext dbContext)
{
    public IQueryable<Clinic> Clinics() =>
        dbContext.Clinics.AsNoTracking().Where(value => value.IsPublished);

    public IQueryable<ClinicLocation> ClinicLocations() =>
        dbContext.ClinicLocations.AsNoTracking().Where(location =>
            location.IsPublished &&
            dbContext.Clinics.Any(clinic =>
                clinic.Id == location.ClinicId && clinic.IsPublished));

    public IQueryable<Doctor> Doctors() =>
        dbContext.Doctors.AsNoTracking().Where(value => value.IsPublished);

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
}
