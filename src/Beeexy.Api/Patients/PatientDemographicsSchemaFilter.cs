using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Beeexy.Api.Patients;

internal sealed class PatientDemographicsSchemaFilter : ISchemaFilter
{
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
        if (!PatientContractTypes.Contains(context.Type) ||
            schema is not OpenApiSchema concreteSchema ||
            concreteSchema.Properties is null ||
            !concreteSchema.Properties.TryGetValue(
                "sexAssignedAtBirth",
                out var sexSchema) ||
            sexSchema is not OpenApiSchema concreteSexSchema)
        {
            return;
        }

        concreteSexSchema.Enum =
        [
            JsonValue.Create("Male"),
            JsonValue.Create("Female")
        ];
        concreteSexSchema.Description =
            "Biological sex assigned at birth. Supported values: Male, Female.";
    }
}
