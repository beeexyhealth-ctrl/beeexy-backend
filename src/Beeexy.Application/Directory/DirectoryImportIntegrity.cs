using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beeexy.Application.Directory;

public static class DirectoryImportIntegrity
{
    public static string Calculate(DirectoryImportPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("packageCode", package.PackageCode.Value);
            writer.WriteString("version", package.Version.Value);
            WriteClinics(writer, package);
            WriteLocations(writer, package);
            WriteDoctors(writer, package);
            WriteAffiliations(writer, package);
            WriteCredentials(writer, package);
            WriteCatalog(writer, "specialties", package.Specialties.Select(value =>
                (value.Id.Value, value.Code.Value, value.Name.Value, value.CreatedAt)));
            WriteLinks(writer, "doctorSpecialties", package.DoctorSpecialties.Select(value =>
                (value.Id.Value, value.DoctorId.Value, value.SpecialtyId.Value, value.CreatedAt)));
            WriteCatalog(writer, "languages", package.Languages.Select(value =>
                (value.Id.Value, value.Code.Value, value.Name.Value, value.CreatedAt)));
            WriteLinks(writer, "doctorLanguages", package.DoctorLanguages.Select(value =>
                (value.Id.Value, value.DoctorId.Value, value.LanguageId.Value, value.CreatedAt)));
            WriteCatalog(writer, "insurancePlans", package.InsurancePlans.Select(value =>
                (value.Id.Value, value.Code.Value, value.Name.Value, value.CreatedAt)));
            WriteLinks(
                writer,
                "doctorInsuranceParticipations",
                package.DoctorInsuranceParticipations.Select(value =>
                    (value.Id.Value, value.DoctorId.Value, value.InsurancePlanId.Value, value.CreatedAt)));
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteClinics(Utf8JsonWriter writer, DirectoryImportPackage package)
    {
        writer.WritePropertyName("clinics");
        writer.WriteStartArray();
        foreach (var value in package.Clinics.OrderBy(value => value.Id.Value))
        {
            writer.WriteStartObject();
            WriteIdentity(writer, value.Id.Value, value.Code.Value, value.Name.Value, value.CreatedAt);
            writer.WriteBoolean("published", value.IsPublished);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLocations(Utf8JsonWriter writer, DirectoryImportPackage package)
    {
        writer.WritePropertyName("clinicLocations");
        writer.WriteStartArray();
        foreach (var value in package.ClinicLocations.OrderBy(value => value.Id.Value))
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id.Value);
            writer.WriteString("clinicId", value.ClinicId.Value);
            writer.WriteString("name", value.Name.Value);
            writer.WriteString("locality", value.Locality);
            writer.WriteString("administrativeArea", value.AdministrativeArea);
            writer.WriteString("country", value.Country);
            writer.WriteString("timezone", value.TimeZone.Value);
            writer.WriteBoolean("published", value.IsPublished);
            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDoctors(Utf8JsonWriter writer, DirectoryImportPackage package)
    {
        writer.WritePropertyName("doctors");
        writer.WriteStartArray();
        foreach (var value in package.Doctors.OrderBy(value => value.Id.Value))
        {
            writer.WriteStartObject();
            WriteIdentity(
                writer,
                value.Id.Value,
                value.Code.Value,
                value.DisplayName.Value,
                value.CreatedAt);
            writer.WriteBoolean("published", value.IsPublished);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteAffiliations(Utf8JsonWriter writer, DirectoryImportPackage package)
    {
        writer.WritePropertyName("doctorAffiliations");
        writer.WriteStartArray();
        foreach (var value in package.DoctorAffiliations.OrderBy(value => value.Id.Value))
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id.Value);
            writer.WriteString("doctorId", value.DoctorId.Value);
            writer.WriteString("clinicId", value.ClinicId.Value);
            if (value.ClinicLocationId.HasValue)
            {
                writer.WriteString("clinicLocationId", value.ClinicLocationId.Value.Value);
            }
            else
            {
                writer.WriteNull("clinicLocationId");
            }

            writer.WriteBoolean("published", value.IsPublished);
            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteCredentials(Utf8JsonWriter writer, DirectoryImportPackage package)
    {
        writer.WritePropertyName("doctorCredentials");
        writer.WriteStartArray();
        foreach (var value in package.DoctorCredentials.OrderBy(value => value.Id.Value))
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id.Value);
            writer.WriteString("doctorId", value.DoctorId.Value);
            writer.WriteString("name", value.Name.Value);
            writer.WriteString("status", value.Status.ToString());
            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteCatalog(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<(Guid Id, string Code, string Name, DateTimeOffset CreatedAt)> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values.OrderBy(value => value.Id))
        {
            writer.WriteStartObject();
            WriteIdentity(writer, value.Id, value.Code, value.Name, value.CreatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLinks(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<(Guid Id, Guid DoctorId, Guid TargetId, DateTimeOffset CreatedAt)> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values.OrderBy(value => value.Id))
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("doctorId", value.DoctorId);
            writer.WriteString("targetId", value.TargetId);
            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteIdentity(
        Utf8JsonWriter writer,
        Guid id,
        string code,
        string name,
        DateTimeOffset createdAt)
    {
        writer.WriteString("id", id);
        writer.WriteString("code", code);
        writer.WriteString("name", name);
        writer.WriteString("createdAt", createdAt);
    }
}
