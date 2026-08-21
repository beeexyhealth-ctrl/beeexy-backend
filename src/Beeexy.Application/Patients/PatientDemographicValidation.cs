using System.Globalization;
using Beeexy.Application.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

internal static class PatientDemographicValidation
{
    private const string DateFormat = "yyyy-MM-dd";

    public static PatientName ParseRequiredName(string? value, string fieldName)
    {
        try
        {
            return PatientName.Create(value ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw Invalid(fieldName, $"A valid {fieldName} is required.");
        }
    }

    public static DateOnly ParseRequiredDateOfBirth(
        string? value,
        DateTimeOffset currentTime)
    {
        if (!DateOnly.TryParseExact(
                value,
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateOfBirth) ||
            dateOfBirth > DateOnly.FromDateTime(currentTime.UtcDateTime))
        {
            throw Invalid(
                "date_of_birth",
                "Date of birth must be a valid non-future ISO date (YYYY-MM-DD).");
        }

        return dateOfBirth;
    }

    public static SexAssignedAtBirth ParseRequiredSexAssignedAtBirth(string? value)
    {
        foreach (var supported in Enum.GetValues<SexAssignedAtBirth>())
        {
            if (string.Equals(value?.Trim(), supported.ToString(), StringComparison.Ordinal))
            {
                return supported;
            }
        }

        throw Invalid(
            "sex_assigned_at_birth",
            "Sex assigned at birth must be Male or Female.");
    }

    public static UsState ParseRequiredState(string? value)
    {
        try
        {
            return UsState.Create(value ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw Invalid(
                "state",
                "State must be a valid two-letter U.S. state code.");
        }
    }

    private static RequestValidationException Invalid(string fieldName, string message) =>
        new($"patient.invalid_{fieldName}", message);
}
