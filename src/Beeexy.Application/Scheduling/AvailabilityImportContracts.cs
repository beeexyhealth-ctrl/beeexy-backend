using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed class AvailabilityImportPackage
{
    private AvailabilityImportPackage(
        DirectoryCode packageCode,
        DirectoryCode version,
        DateOnly referenceDate,
        AvailabilitySlot[] slots)
    {
        PackageCode = packageCode;
        Version = version;
        ReferenceDate = referenceDate;
        Slots = Array.AsReadOnly(slots);
        ContentHash = AvailabilityImportIntegrity.Calculate(this);
    }

    public DirectoryCode PackageCode { get; }

    public DirectoryCode Version { get; }

    public DateOnly ReferenceDate { get; }

    public IReadOnlyList<AvailabilitySlot> Slots { get; }

    public string ContentHash { get; }

    public static AvailabilityImportPackage Create(
        DirectoryCode packageCode,
        DirectoryCode version,
        DateOnly referenceDate,
        IEnumerable<AvailabilitySlot> slots)
    {
        ArgumentNullException.ThrowIfNull(packageCode);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(slots);
        var values = slots.ToArray();
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Availability packages cannot contain null slots.", nameof(slots));
        }

        return new AvailabilityImportPackage(packageCode, version, referenceDate, values);
    }
}

public static class AvailabilityImportIntegrity
{
    public static string Calculate(AvailabilityImportPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("packageCode", package.PackageCode.Value);
            writer.WriteString("version", package.Version.Value);
            writer.WriteString(
                "referenceDate",
                package.ReferenceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WritePropertyName("slots");
            writer.WriteStartArray();
            foreach (var slot in package.Slots.OrderBy(value => value.Id.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("id", slot.Id.Value);
                writer.WriteString("doctorId", slot.DoctorId.Value);
                writer.WriteString("clinicId", slot.ClinicId.Value);
                writer.WriteString("locationId", slot.ClinicLocationId.Value);
                writer.WriteString("startsAt", slot.StartsAt);
                writer.WriteString("endsAt", slot.EndsAt);
                writer.WriteString("clinicTimeZone", slot.ClinicTimeZone.Value);
                writer.WriteString("modality", slot.Modality.ToString());
                writer.WriteBoolean("published", slot.IsPublished);
                writer.WriteString("createdAt", slot.CreatedAt);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}

public sealed class AvailabilityImportPackageValidator
{
    public void Validate(AvailabilityImportPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Slots.Count == 0)
        {
            throw new AvailabilityImportValidationException(
                "The availability package must contain slots.");
        }

        if (package.Slots.Select(value => value.Id).Distinct().Count() != package.Slots.Count)
        {
            throw new AvailabilityImportValidationException(
                "Availability package slot identifiers must be unique.");
        }

        if (!string.Equals(
            package.ContentHash,
            AvailabilityImportIntegrity.Calculate(package),
            StringComparison.Ordinal))
        {
            throw new AvailabilityImportValidationException(
                "Availability package content does not match its immutable content hash.");
        }
    }
}

public enum AvailabilityImportOutcome
{
    Imported,
    AlreadyImported
}

public sealed record AvailabilityImportResult(
    AvailabilityImportOutcome Outcome,
    DirectoryCode PackageCode,
    DirectoryCode Version,
    DateOnly ReferenceDate,
    string ContentHash);

public interface IAvailabilityImporter
{
    Task<AvailabilityImportResult> ImportAsync(
        AvailabilityImportPackage package,
        CancellationToken cancellationToken = default);
}

public sealed class AvailabilityImportValidationException(string message) : Exception(message);

public sealed class AvailabilityImportConflictException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
