using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Beeexy.Api.Patients;

internal sealed class PatientDemographicsSchemaFilter : ISchemaFilter
{
    private static readonly string[] RelationshipTypes =
    [
        "Parent",
        "LegalGuardian",
        "Caregiver",
        "Spouse",
        "Child",
        "Sibling",
        "Other"
    ];

    private static readonly string[] RelationshipStatuses = ["Active", "Revoked"];

    private static readonly HashSet<Type> PatientContractTypes =
    [
        typeof(ManagedPatientRequest),
        typeof(UpdateManagedPatientRequest),
        typeof(PatientProfileResponse),
        typeof(CreatedManagedPatientResponse),
        typeof(PrimaryProfileResponse)
    ];

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concreteSchema ||
            concreteSchema.Properties is null)
        {
            return;
        }

        if (PatientContractTypes.Contains(context.Type))
        {
            ApplyEnum(
                concreteSchema,
                "sexAssignedAtBirth",
                ["Male", "Female"],
                "Biological sex assigned at birth. Supported values: Male, Female.");
        }

        if (context.Type == typeof(CreateManagedPatientRequest))
        {
            ApplyEnum(
                concreteSchema,
                "relationshipType",
                RelationshipTypes,
                "My Circle management relationship type.");
        }

        if (context.Type == typeof(AccessiblePatientRelationshipResponse) ||
            context.Type == typeof(CareRelationshipResponse) ||
            context.Type == typeof(CreatedCareRelationshipResponse))
        {
            ApplyEnum(
                concreteSchema,
                "type",
                RelationshipTypes,
                "My Circle management relationship type.");
        }

        if (context.Type == typeof(CareRelationshipResponse) ||
            context.Type == typeof(CreatedCareRelationshipResponse))
        {
            ApplyEnum(
                concreteSchema,
                "status",
                RelationshipStatuses,
                "Relationship lifecycle status.");
        }
    }

    private static void ApplyEnum(
        OpenApiSchema schema,
        string propertyName,
        IReadOnlyCollection<string> values,
        string description)
    {
        if (!schema.Properties!.TryGetValue(propertyName, out var property) ||
            property is not OpenApiSchema concreteProperty)
        {
            return;
        }

        concreteProperty.Enum = values
            .Select(value => (JsonNode)JsonValue.Create(value))
            .ToList();
        concreteProperty.Description = description;
    }
}
