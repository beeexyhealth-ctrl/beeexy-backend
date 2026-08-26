using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public sealed record DemoGuestDefinition(
    NormalizedEmail Email,
    PatientName FirstName,
    PatientName LastName,
    DateOnly DateOfBirth,
    SexAssignedAtBirth SexAssignedAtBirth,
    UsState State,
    UserTimeZone TimeZone);
