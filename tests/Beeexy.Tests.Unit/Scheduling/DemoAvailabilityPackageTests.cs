using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.Scheduling;

namespace Beeexy.Tests.Unit.Scheduling;

[Trait("Category", "Phase8Acceptance")]
public sealed class DemoAvailabilityPackageTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 8, 31);

    [Fact]
    public void ProductPackage_IsDeterministicImmutableAndUsesApprovedDirectoryReferences()
    {
        var first = ProductApprovedSyntheticAvailability.Create(ReferenceDate);
        var second = ProductApprovedSyntheticAvailability.Create(ReferenceDate);

        Assert.Equal(ProductApprovedSyntheticAvailability.ExpectedContentHash, first.ContentHash);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(ProductApprovedSyntheticAvailability.SlotCount, first.Slots.Count);
        Assert.Equal(
            first.Slots.Select(value => value.Id),
            second.Slots.Select(value => value.Id));
        Assert.All(first.Slots, slot =>
        {
            Assert.Equal(TimeSpan.FromMinutes(30), slot.Duration);
            Assert.Equal("America/Lima", slot.ClinicTimeZone.Value);
            Assert.True(slot.IsPublished);
            Assert.StartsWith("71020000-0000-42", slot.DoctorId.Value.ToString());
            Assert.StartsWith("71020000-0000-40", slot.ClinicId.Value.ToString());
            Assert.StartsWith("71020000-0000-41", slot.ClinicLocationId.Value.ToString());
        });
        Assert.Contains(first.Slots, value => value.Modality == AppointmentModality.InPerson);
        Assert.Contains(first.Slots, value => value.Modality == AppointmentModality.Virtual);
    }

    [Fact]
    public void DifferentReferenceDate_ProducesDifferentExplicitSlotsAtSameLocalTimes()
    {
        var first = ProductApprovedSyntheticAvailability.Create(ReferenceDate);
        var next = ProductApprovedSyntheticAvailability.Create(ReferenceDate.AddDays(1));

        Assert.NotEqual(first.ContentHash, next.ContentHash);
        Assert.Empty(first.Slots.Select(value => value.Id).Intersect(next.Slots.Select(value => value.Id)));
        Assert.Equal(
            first.Slots.Select(value => value.StartsAt.AddDays(1)),
            next.Slots.Select(value => value.StartsAt));
    }
}
