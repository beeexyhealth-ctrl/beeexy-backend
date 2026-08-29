using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class ClinicLocation
{
    public const int MaximumLocationPartLength = 100;

    private ClinicLocation()
    {
        Name = null!;
        Locality = null!;
        AdministrativeArea = null!;
        Country = null!;
        TimeZone = null!;
    }

    private ClinicLocation(
        EntityId id,
        EntityId clinicId,
        DirectoryName name,
        string locality,
        string administrativeArea,
        string country,
        IanaTimeZone timeZone,
        bool isPublished,
        DateTimeOffset createdAt)
    {
        Id = id;
        ClinicId = clinicId;
        Name = name;
        Locality = locality;
        AdministrativeArea = administrativeArea;
        Country = country;
        TimeZone = timeZone;
        IsPublished = isPublished;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId ClinicId { get; private set; }

    public DirectoryName Name { get; private set; }

    public string Locality { get; private set; }

    public string AdministrativeArea { get; private set; }

    public string Country { get; private set; }

    public IanaTimeZone TimeZone { get; private set; }

    public bool IsPublished { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static ClinicLocation Create(
        EntityId clinicId,
        DirectoryName name,
        string locality,
        string administrativeArea,
        string country,
        IanaTimeZone timeZone,
        bool isPublished,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        DirectoryValueGuard.EnsureNonEmpty(clinicId, nameof(clinicId));
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(timeZone);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));

        return new ClinicLocation(
            entityId,
            clinicId,
            name,
            DirectoryValueGuard.RequiredText(
                locality,
                MaximumLocationPartLength,
                nameof(locality)),
            DirectoryValueGuard.RequiredText(
                administrativeArea,
                MaximumLocationPartLength,
                nameof(administrativeArea)),
            DirectoryValueGuard.RequiredText(
                country,
                MaximumLocationPartLength,
                nameof(country)),
            timeZone,
            isPublished,
            createdAt);
    }
}
