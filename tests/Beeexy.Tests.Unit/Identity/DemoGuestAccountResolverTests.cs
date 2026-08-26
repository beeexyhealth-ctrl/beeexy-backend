using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Identity;

public sealed class DemoGuestAccountResolverTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        26,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CompleteMatchingPrimaryIdentity_IsResolved()
    {
        var (definition, state) = CompleteState();

        var resolved = DemoGuestAccountResolver.TryResolve(definition, state);

        Assert.NotNull(resolved);
        Assert.Same(state.Account, resolved.Account);
        Assert.Same(state.PrimaryProfiles[0], resolved.PrimaryProfile);
        Assert.Same(state.Preferences[0], resolved.Preference);
    }

    [Fact]
    public void DisabledOrDemographicallyDifferentIdentity_IsRejected()
    {
        var (definition, state) = CompleteState();
        state.Account!.Disable(Now.AddMinutes(1));
        Assert.Null(DemoGuestAccountResolver.TryResolve(definition, state));

        var (activeDefinition, activeState) = CompleteState();
        activeState.PrimaryProfiles[0].UpdateDemographics(
            PatientName.Create("Different"),
            null,
            null,
            null,
            null,
            Now.AddMinutes(1));
        Assert.Null(DemoGuestAccountResolver.TryResolve(activeDefinition, activeState));
    }

    [Fact]
    public void MissingOrAmbiguousProfileState_IsRejected()
    {
        var (definition, state) = CompleteState();

        Assert.Null(DemoGuestAccountResolver.TryResolve(
            definition,
            state with { PrimaryProfiles = [] }));
        Assert.Null(DemoGuestAccountResolver.TryResolve(
            definition,
            state with { Preferences = [] }));
    }

    private static (DemoGuestDefinition Definition, DemoGuestAccountState State) CompleteState()
    {
        var definition = new DemoGuestDefinition(
            NormalizedEmail.Create("demo-resolver@example.com"),
            PatientName.Create("Bee"),
            PatientName.Create("Exy"),
            new DateOnly(1990, 5, 20),
            SexAssignedAtBirth.Female,
            UsState.Create("CA"),
            UserTimeZone.Create("America/Lima"));
        var account = Account.Create(definition.Email, Now);
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            Now,
            account.Id);
        profile.UpdateDemographics(
            definition.FirstName,
            definition.LastName,
            definition.DateOfBirth,
            definition.SexAssignedAtBirth,
            definition.State,
            Now);
        var preference = UserPreference.Create(account.Id, definition.TimeZone, Now);

        return (
            definition,
            new DemoGuestAccountState(account, [profile], [preference]));
    }
}
