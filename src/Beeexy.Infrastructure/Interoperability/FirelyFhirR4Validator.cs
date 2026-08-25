using System.Text;
using System.Text.Json;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Interoperability;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Validation;
using SystemTasks = System.Threading.Tasks;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FirelyFhirR4Validator : IFhirValidator
{
    private static readonly FhirValidatorMetadata Metadata =
        FhirValidatorMetadata.Create("Firely .NET SDK R4 POCO validator", "6.4.0");

    public SystemTasks.Task<FhirValidatorExecutionResult> ValidateAsync(
        FhirValidatorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Matches(request.Specification))
        {
            return SystemTasks.Task.FromResult(
                FhirValidatorExecutionResult.UnsupportedSpecification());
        }

        try
        {
            var json = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(request.ArtifactBytes.Span);
            var resource = FhirJsonDeserializer.STRICT.DeserializeResource(json);
            if (resource is not Bundle bundle)
            {
                return SystemTasks.Task.FromResult(Invalid("expected-collection-bundle"));
            }

            var diagnostics = bundle.Validate()
                .Select(_ => Error("firely-r4-base"))
                .Concat(ValidateClosedBundle(bundle))
                .ToArray();
            if (diagnostics.Any(value =>
                value.Severity == FhirValidationDiagnosticSeverity.Error))
            {
                return SystemTasks.Task.FromResult(
                    FhirValidatorExecutionResult.Invalid(Metadata, diagnostics));
            }

            return SystemTasks.Task.FromResult(FhirValidatorExecutionResult.Valid(
                Metadata,
                [new FhirValidatorDiagnostic(
                    FhirValidationDiagnosticSeverity.Warning,
                    "external-terminology-not-executed",
                    null)]));
        }
        catch (Exception exception) when (exception is DecoderFallbackException or
            JsonException or DeserializationFailedException)
        {
            return SystemTasks.Task.FromResult(Invalid("invalid-fhir-r4-json"));
        }
    }

    private static IEnumerable<FhirValidatorDiagnostic> ValidateClosedBundle(Bundle bundle)
    {
        if (bundle.Type != Bundle.BundleType.Collection)
        {
            yield return Error("expected-collection-bundle");
        }

        if (bundle.Meta?.Profile?.Any() == true)
        {
            yield return Error("profiles-not-allowed");
        }

        var entries = bundle.Entry?.ToArray() ?? [];
        var fullUrls = entries.Select(value => value.FullUrl).ToArray();
        if (fullUrls.Any(string.IsNullOrWhiteSpace) ||
            fullUrls.Distinct(StringComparer.Ordinal).Count() != fullUrls.Length)
        {
            yield return Error("invalid-bundle-fullurl");
        }

        foreach (var entry in entries)
        {
            var resource = entry.Resource;
            if (resource is null ||
                string.IsNullOrWhiteSpace(resource.Id) ||
                !Guid.TryParse(resource.Id, out _) ||
                !string.Equals(
                    entry.FullUrl,
                    $"urn:uuid:{resource.Id}",
                    StringComparison.Ordinal))
            {
                yield return Error("invalid-bundle-identity");
            }

            if (resource?.Meta?.Profile?.Any() == true)
            {
                yield return Error("profiles-not-allowed");
            }
        }

        if (entries.Count(value => value.Resource is QuestionnaireResponse) != 1 ||
            entries.Count(value => value.Resource is Device) != 1 ||
            entries.Count(value => value.Resource is Provenance) != 1 ||
            entries.Length != 3)
        {
            yield return Error("invalid-r4-mvp-resource-set");
        }

        var responseEntry = entries.SingleOrDefault(value =>
            value.Resource is QuestionnaireResponse);
        var deviceEntry = entries.SingleOrDefault(value => value.Resource is Device);
        var provenanceEntry = entries.SingleOrDefault(value =>
            value.Resource is Provenance);
        if (responseEntry?.Resource is QuestionnaireResponse response &&
            (response.Status != QuestionnaireResponse.QuestionnaireResponseStatus.Completed ||
             response.Subject is not null ||
             response.Questionnaire is not null))
        {
            yield return Error("invalid-r4-mvp-questionnaire-response");
        }

        if (provenanceEntry?.Resource is Provenance provenance &&
            (provenance.Target?.Count != 1 ||
             provenance.Target[0].Reference != responseEntry?.FullUrl ||
             provenance.Agent?.Count != 1 ||
             provenance.Agent[0].Who?.Reference != deviceEntry?.FullUrl ||
             provenance.Entity?.Count != 1 ||
             provenance.Entity[0].What?.Reference != responseEntry?.FullUrl))
        {
            yield return Error("invalid-r4-mvp-provenance");
        }

        var known = fullUrls.Where(value => value is not null)
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var reference in References(entries))
        {
            if (string.IsNullOrWhiteSpace(reference) ||
                !reference!.StartsWith("urn:uuid:", StringComparison.Ordinal) ||
                !known.Contains(reference))
            {
                yield return Error("unresolved-bundle-reference");
            }
        }
    }

    private static IEnumerable<string?> References(
        IEnumerable<Bundle.EntryComponent> entries)
    {
        foreach (var provenance in entries.Select(value => value.Resource)
            .OfType<Provenance>())
        {
            foreach (var target in provenance.Target ?? [])
            {
                yield return target.Reference;
            }

            foreach (var agent in provenance.Agent ?? [])
            {
                yield return agent.Who?.Reference!;
                if (agent.OnBehalfOf is not null)
                {
                    yield return agent.OnBehalfOf.Reference;
                }
            }

            foreach (var entity in provenance.Entity ?? [])
            {
                yield return entity.What?.Reference!;
            }

            if (provenance.Location is not null)
            {
                yield return provenance.Location.Reference;
            }
        }

        foreach (var response in entries.Select(value => value.Resource)
            .OfType<QuestionnaireResponse>())
        {
            foreach (var basedOn in response.BasedOn ?? [])
            {
                yield return basedOn.Reference;
            }

            foreach (var partOf in response.PartOf ?? [])
            {
                yield return partOf.Reference;
            }

            if (response.Subject is not null)
            {
                yield return response.Subject.Reference;
            }

            if (response.Encounter is not null)
            {
                yield return response.Encounter.Reference;
            }
        }

        foreach (var device in entries.Select(value => value.Resource).OfType<Device>())
        {
            if (device.Patient is not null)
            {
                yield return device.Patient.Reference;
            }

            if (device.Owner is not null)
            {
                yield return device.Owner.Reference;
            }

            if (device.Location is not null)
            {
                yield return device.Location.Reference;
            }

            if (device.Parent is not null)
            {
                yield return device.Parent.Reference;
            }
        }
    }

    private static bool Matches(FhirValidationSpecification specification) =>
        string.Equals(
            specification.FhirRelease,
            FhirR4BaseMvp.FhirRelease,
            StringComparison.Ordinal) &&
        string.Equals(
            specification.MappingVersion,
            FhirR4BaseMvp.MappingVersion,
            StringComparison.Ordinal) &&
        specification.ProfileResolution.Status ==
            FhirProfileResolutionStatus.NotApplicable;

    private static FhirValidatorExecutionResult Invalid(string code) =>
        FhirValidatorExecutionResult.Invalid(Metadata, [Error(code)]);

    private static FhirValidatorDiagnostic Error(string code) => new(
        FhirValidationDiagnosticSeverity.Error,
        code,
        null);
}
