using Beeexy.Application.Interoperability;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class DeviceMapperTests
{
    [Fact]
    public void Map_GeneratesAndreaSupportedBeeexySoftwareIdentity()
    {
        var specification = Specification();
        var input = DeviceMappingInput.Create("runtime-version-2026.08.24");

        var representation = new DeviceMapper(specification).Map(input);

        Assert.Equal(FhirConceptualResource.Device, representation.Resource);
        Assert.Equal("Beeexy Triage Engine", representation.DeviceName.Name);
        Assert.Equal("manufacturer-name", representation.DeviceName.Type);
        Assert.Equal("triage-core", representation.ModelNumber);
        Assert.Equal("runtime-version-2026.08.24", representation.Version.Value);
        Assert.Equal("Beeexy Inc.", representation.Manufacturer);
        Assert.Equal("Clinical decision support software", representation.TypeText);
        Assert.Same(specification, representation.MappingSpecification);
    }

    [Fact]
    public void Map_IdenticalInputIsDeterministicAndInputIsUnchanged()
    {
        var input = DeviceMappingInput.Create("immutable-runtime-version");
        var before = input with { };
        var mapper = new DeviceMapper(Specification());

        var first = mapper.Map(input);
        var second = mapper.Map(input);

        Assert.Equal(first.DeviceName, second.DeviceName);
        Assert.Equal(first.ModelNumber, second.ModelNumber);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Manufacturer, second.Manufacturer);
        Assert.Equal(first.TypeText, second.TypeText);
        Assert.Equal(first.UnresolvedRequirements, second.UnresolvedRequirements);
        Assert.Equal(before, input);
    }

    [Fact]
    public void Representation_DoesNotInventHardwareRegulatoryOrIdentifierFields()
    {
        var representation = new DeviceMapper(Specification()).Map(
            DeviceMappingInput.Create("version-from-runtime"));
        var propertyNames = typeof(DeviceRepresentation).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Udi", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Serial", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Hardware", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Regulat", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Fda", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Owner", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Identifier", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Canonical", StringComparison.OrdinalIgnoreCase));
        Assert.Null(representation.LogicalId);
        Assert.Null(representation.FinalReference);
    }

    [Fact]
    public void Representation_IsProcessingSoftwareAndCarriesNoPatientDeviceInput()
    {
        var constructor = Assert.Single(typeof(DeviceMapper).GetConstructors());
        Assert.Equal(
            typeof(FhirMappingSpecificationIdentity),
            Assert.Single(constructor.GetParameters()).ParameterType);
        Assert.DoesNotContain(
            typeof(DeviceMappingInput).GetProperties(),
            property => property.Name.Contains("Patient", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Hardware", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Map_LeavesReleaseProfilesAndFinalReferenceStrategyUnresolved()
    {
        var representation = new DeviceMapper(Specification()).Map(
            DeviceMappingInput.Create("version-from-runtime"));

        Assert.Equal(
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy
            ],
            representation.UnresolvedRequirements);
        Assert.False(representation.CanSerializeAsFhir);
    }

    private static FhirMappingSpecificationIdentity Specification() =>
        FhirMappingSpecificationIdentity.Create("phase-6.4-test");
}
