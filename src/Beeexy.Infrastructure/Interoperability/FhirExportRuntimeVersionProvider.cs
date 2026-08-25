using System.Reflection;
using Beeexy.Application.Interoperability;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FhirExportRuntimeVersionProvider
    : IFhirExportRuntimeVersionProvider
{
    public string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(FhirExportRuntimeVersionProvider).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var value = informational?.Split('+', 2)[0] ??
            assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "The Beeexy backend runtime version is unavailable.")
            : value;
    }
}
