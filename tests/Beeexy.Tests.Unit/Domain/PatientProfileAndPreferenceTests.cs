using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Domain;

public sealed class PatientProfileAndPreferenceTests
{
    [Fact]
    public void PatientProfile_KeepsBeeexyIdImmutable_AndAllowsNoAccountOwner()
    {
        var beeexyId = BeeexyId.Create("BXY-000001");

        var profile = PatientProfile.Create(beeexyId, Utc(12));

        Assert.Null(profile.AccountId);
        Assert.Same(beeexyId, profile.BeeexyId);
        Assert.False(typeof(PatientProfile)
            .GetProperty(nameof(PatientProfile.BeeexyId))!
            .SetMethod!
            .IsPublic);
    }

    [Fact]
    public void PatientProfile_CanBeCreatedForAnAccount()
    {
        var accountId = EntityId.New();

        var profile = PatientProfile.Create(
            BeeexyId.Create("BXY-000002"),
            Utc(12),
            accountId);

        Assert.Equal(accountId, profile.AccountId);
    }

    [Theory]
    [InlineData("America/Lima")]
    [InlineData("Europe/Madrid")]
    [InlineData("Etc/UTC")]
    public void UserTimeZone_Create_AcceptsRecognizedIanaIdentifier(string identifier)
    {
        Assert.Equal(identifier, UserTimeZone.Create(identifier).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not/A_Real_Zone")]
    [InlineData("Pacific Standard Time")]
    public void UserTimeZone_Create_RejectsInvalidOrNonIanaIdentifier(string identifier)
    {
        Assert.Throws<ArgumentException>(() => UserTimeZone.Create(identifier));
    }

    [Fact]
    public void UserPreference_ChangesTimezoneAndRecordsUpdateInstant()
    {
        var createdAt = Utc(12);
        var preference = UserPreference.Create(
            EntityId.New(),
            UserTimeZone.Create("America/Lima"),
            createdAt);

        Assert.Equal(1, preference.Version);

        preference.ChangeTimeZone(
            UserTimeZone.Create("Europe/Madrid"),
            createdAt.AddMinutes(1));

        Assert.Equal("Europe/Madrid", preference.TimeZone.Value);
        Assert.Equal(2, preference.Version);
        Assert.Equal(createdAt.AddMinutes(1), preference.UpdatedAt);
    }

    [Fact]
    public void UserPreference_UnchangedTimezone_DoesNotAdvanceVersion()
    {
        var createdAt = Utc(12);
        var preference = UserPreference.Create(
            EntityId.New(),
            UserTimeZone.Create("America/Lima"),
            createdAt);

        preference.ChangeTimeZone(
            UserTimeZone.Create("America/Lima"),
            createdAt.AddMinutes(1));

        Assert.Equal(1, preference.Version);
        Assert.Null(preference.UpdatedAt);
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 19, hour, 0, 0, TimeSpan.Zero);
    }
}
