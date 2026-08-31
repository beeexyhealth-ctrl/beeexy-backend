using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Infrastructure.Scheduling;

public static class ProductApprovedSyntheticAvailability
{
    public const string PackageCode = "beeexy-synthetic-demo-availability";
    public const string Version = "2026.08.31-demo.1";
    public const string ExpectedContentHash =
        "5cf4c6b690aec4055cc9b386753fb5990d7ddd799419191639f60e8683406536";
    public const int SlotCount = 6;

    private const string ClinicTimeZone = "America/Lima";
    private static readonly EntityId AuroraClinicId = Id("71020000-0000-4000-8000-000000000001");
    private static readonly EntityId MosaicClinicId = Id("71020000-0000-4000-8000-000000000002");
    private static readonly EntityId AuroraLocationId = Id("71020000-0000-4100-8000-000000000011");
    private static readonly EntityId MosaicLocationId = Id("71020000-0000-4100-8000-000000000013");
    private static readonly EntityId AmberDoctorId = Id("71020000-0000-4200-8000-000000000021");
    private static readonly EntityId BlueDoctorId = Id("71020000-0000-4200-8000-000000000022");

    public static AvailabilityImportPackage Create(DateOnly referenceDate)
    {
        var createdAt = new DateTimeOffset(
            referenceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var slots = new[]
        {
            Slot("amber-day-1-0900-in-person", AmberDoctorId, AuroraClinicId,
                AuroraLocationId, referenceDate.AddDays(1), new TimeOnly(9, 0),
                AppointmentModality.InPerson, createdAt),
            Slot("amber-day-1-0930-virtual", AmberDoctorId, AuroraClinicId,
                AuroraLocationId, referenceDate.AddDays(1), new TimeOnly(9, 30),
                AppointmentModality.Virtual, createdAt),
            Slot("blue-day-2-1000-in-person", BlueDoctorId, MosaicClinicId,
                MosaicLocationId, referenceDate.AddDays(2), new TimeOnly(10, 0),
                AppointmentModality.InPerson, createdAt),
            Slot("blue-day-2-1030-virtual", BlueDoctorId, MosaicClinicId,
                MosaicLocationId, referenceDate.AddDays(2), new TimeOnly(10, 30),
                AppointmentModality.Virtual, createdAt),
            Slot("amber-day-7-1500-in-person", AmberDoctorId, AuroraClinicId,
                AuroraLocationId, referenceDate.AddDays(7), new TimeOnly(15, 0),
                AppointmentModality.InPerson, createdAt),
            Slot("blue-day-7-1600-virtual", BlueDoctorId, MosaicClinicId,
                MosaicLocationId, referenceDate.AddDays(7), new TimeOnly(16, 0),
                AppointmentModality.Virtual, createdAt)
        };
        var package = AvailabilityImportPackage.Create(
            DirectoryCode.Create(PackageCode),
            DirectoryCode.Create(Version),
            referenceDate,
            slots);
        if (referenceDate == new DateOnly(2026, 8, 31) &&
            !string.Equals(package.ContentHash, ExpectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The approved synthetic availability content changed. Review the dataset and " +
                $"assign a new version and expected content hash. Actual: {package.ContentHash}");
        }

        return package;
    }

    private static AvailabilitySlot Slot(
        string key,
        EntityId doctorId,
        EntityId clinicId,
        EntityId locationId,
        DateOnly localDate,
        TimeOnly localTime,
        AppointmentModality modality,
        DateTimeOffset createdAt)
    {
        var startsAt = ToUtc(localDate, localTime, ClinicTimeZone);
        return AvailabilitySlot.Create(
            doctorId,
            clinicId,
            locationId,
            startsAt,
            startsAt.AddMinutes(30),
            IanaTimeZone.Create(ClinicTimeZone),
            modality,
            true,
            createdAt,
            AvailabilityImporter.CreateDeterministicId(
                $"{PackageCode}@{Version}:{localDate:yyyy-MM-dd}:{key}"));
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string timeZoneId)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
        {
            throw new InvalidOperationException(
                "The synthetic availability package contains an ambiguous clinic-local time.");
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static EntityId Id(string value) => EntityId.From(Guid.Parse(value));
}
