using Beeexy.Application.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Application.Directory;

internal static class DoctorDirectoryInputNormalizer
{
    public static DoctorDirectoryFilter NormalizeFilter(
        string? specialtyCode,
        string? languageCode,
        string? locality,
        string? administrativeArea,
        string? country,
        string? insurancePlanCode,
        string errorCode)
    {
        return new DoctorDirectoryFilter(
            NormalizeCode(specialtyCode, "specialtyCode", errorCode),
            NormalizeCode(languageCode, "languageCode", errorCode),
            NormalizeLocationPart(locality, "locality", errorCode),
            NormalizeLocationPart(administrativeArea, "administrativeArea", errorCode),
            NormalizeLocationPart(country, "country", errorCode),
            NormalizeCode(insurancePlanCode, "insurancePlanCode", errorCode));
    }

    public static string NormalizeRequiredCode(
        string? value,
        string parameterName,
        string errorCode)
    {
        if (value is null)
        {
            throw Invalid(parameterName, errorCode);
        }

        return NormalizeCode(value, parameterName, errorCode)!;
    }

    private static string? NormalizeCode(
        string? value,
        string parameterName,
        string errorCode)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return DirectoryCode.Create(value).Value;
        }
        catch (ArgumentException)
        {
            throw Invalid(parameterName, errorCode);
        }
    }

    private static string? NormalizeLocationPart(
        string? value,
        string parameterName,
        string errorCode)
    {
        if (value is null)
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.Length is 0 or > ClinicLocation.MaximumLocationPartLength)
        {
            throw Invalid(parameterName, errorCode);
        }

        return candidate;
    }

    private static RequestValidationException Invalid(string parameterName, string errorCode) =>
        new(errorCode, $"The {parameterName} value is invalid.");
}
