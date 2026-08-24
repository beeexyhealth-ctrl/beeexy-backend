namespace Beeexy.Application.Interoperability;

public sealed record DeviceNameRepresentation(string Name, string Type);

public sealed record DeviceVersionRepresentation(string Value);

public sealed class DeviceRepresentation
{
    internal DeviceRepresentation(
        DeviceMappingInput source,
        FhirMappingSpecificationIdentity mappingSpecification,
        IReadOnlyList<FhirUnresolvedMappingRequirement> unresolvedRequirements)
    {
        DeviceName = new DeviceNameRepresentation(
            source.DeviceName,
            source.DeviceNameType);
        ModelNumber = source.ModelNumber;
        Version = new DeviceVersionRepresentation(source.SoftwareVersion);
        Manufacturer = source.Manufacturer;
        TypeText = source.TypeText;
        MappingSpecification = mappingSpecification;
        UnresolvedRequirements = unresolvedRequirements;
    }

    public FhirConceptualResource Resource => FhirConceptualResource.Device;

    public DeviceNameRepresentation DeviceName { get; }

    public string ModelNumber { get; }

    public DeviceVersionRepresentation Version { get; }

    public string Manufacturer { get; }

    public string TypeText { get; }

    public string? LogicalId => null;

    public string? FinalReference => null;

    public FhirMappingSpecificationIdentity MappingSpecification { get; }

    public IReadOnlyList<FhirUnresolvedMappingRequirement> UnresolvedRequirements { get; }

    public bool CanSerializeAsFhir => false;
}

public sealed class DeviceMapper :
    IFhirMapper<DeviceMappingInput, DeviceRepresentation>
{
    private readonly FhirMappingSpecificationIdentity _mappingSpecification;

    public DeviceMapper(FhirMappingSpecificationIdentity mappingSpecification)
    {
        ArgumentNullException.ThrowIfNull(mappingSpecification);
        _mappingSpecification = mappingSpecification;
    }

    public DeviceRepresentation Map(DeviceMappingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var unresolved = FhirRepresentationRequirements.From(_mappingSpecification);
        unresolved.Add(
            FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy);

        return new DeviceRepresentation(
            input,
            _mappingSpecification,
            unresolved.AsReadOnly());
    }
}
