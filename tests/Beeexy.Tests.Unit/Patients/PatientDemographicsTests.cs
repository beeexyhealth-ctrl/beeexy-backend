using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class PatientDemographicsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PatientName_TrimsUnicodeText()
    {
        Assert.Equal("María José", PatientName.Create("  María José  ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PatientName_RejectsBlankValue(string value)
    {
        Assert.Throws<ArgumentException>(() => PatientName.Create(value));
    }

    [Fact]
    public void PatientName_RejectsValueBeyondMaximumLength()
    {
        Assert.Throws<ArgumentException>(() =>
            PatientName.Create(new string('a', PatientName.MaximumLength + 1)));
    }

    [Theory]
    [InlineData("ny", "NY")]
    [InlineData(" ca ", "CA")]
    [InlineData("TX", "TX")]
    public void UsState_NormalizesValidPostalCode(string value, string expected)
    {
        Assert.Equal(expected, UsState.Create(value).Code);
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("New York")]
    [InlineData("")]
    public void UsState_RejectsUnsupportedValue(string value)
    {
        Assert.Throws<ArgumentException>(() => UsState.Create(value));
    }

    [Fact]
    public void ManagedProfile_RequiresValidNonFutureDateOfBirth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PatientProfile.CreateManaged(
            BeeexyId.Create("BXY-FUTURE-DOB"),
            PatientName.Create("Maria"),
            PatientName.Create("Arias"),
            new DateOnly(2026, 8, 22),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            Now));
    }

    [Fact]
    public void ManagedProfile_InitializesApprovedDemographicsAndVersion()
    {
        var profile = CreateManaged();

        Assert.Equal("Maria", profile.FirstName?.Value);
        Assert.Equal("Arias", profile.LastName?.Value);
        Assert.Equal(new DateOnly(2012, 5, 12), profile.DateOfBirth);
        Assert.Equal(SexAssignedAtBirth.Female, profile.SexAssignedAtBirth);
        Assert.Equal("NY", profile.State?.Code);
        Assert.Equal(1, profile.Version);
    }

    [Fact]
    public void LegacyOrProvisionedPrimaryProfile_RemainsIncompleteWithSafeVersion()
    {
        var profile = PatientProfile.Create(
            BeeexyId.Create("BXY-INCOMPLETE-PRIMARY"),
            Now);

        Assert.Null(profile.FirstName);
        Assert.Null(profile.LastName);
        Assert.Null(profile.DateOfBirth);
        Assert.Null(profile.SexAssignedAtBirth);
        Assert.Null(profile.State);
        Assert.Equal(1, profile.Version);
    }

    [Fact]
    public void EffectiveUpdate_IncrementsVersionOnceAndTracksCategoriesOnly()
    {
        var profile = CreateManaged();

        var changed = profile.UpdateDemographics(
            PatientName.Create("Ana"),
            null,
            null,
            null,
            UsState.Create("FL"),
            Now.AddMinutes(1));

        Assert.Equal(["firstName", "state"], changed);
        Assert.Equal(2, profile.Version);
        Assert.Equal(Now.AddMinutes(1), profile.UpdatedAt);
    }

    [Fact]
    public void SameValueUpdate_DoesNotChangeVersionOrTimestamp()
    {
        var profile = CreateManaged();

        var changed = profile.UpdateDemographics(
            PatientName.Create("Maria"),
            null,
            null,
            null,
            UsState.Create("ny"),
            Now.AddMinutes(1));

        Assert.Empty(changed);
        Assert.Equal(1, profile.Version);
        Assert.Null(profile.UpdatedAt);
    }

    private static PatientProfile CreateManaged() => PatientProfile.CreateManaged(
        BeeexyId.Create("BXY-MANAGED-DEMOGRAPHICS"),
        PatientName.Create("Maria"),
        PatientName.Create("Arias"),
        new DateOnly(2012, 5, 12),
        SexAssignedAtBirth.Female,
        UsState.Create("NY"),
        Now);
}
