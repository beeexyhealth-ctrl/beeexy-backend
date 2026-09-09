using Beeexy.Domain.Sharing;

namespace Beeexy.Tests.Unit.Domain;

public sealed class Phase111ArchitectureTests
{
    [Fact]
    [Trait("Category", "Phase111")]
    public void SharingFoundation_HasNoExecutionRendererStorageOrRecipientAuthImplementation()
    {
        var sharingTypes = typeof(ShareGrant).Assembly.GetTypes()
            .Where(type => string.Equals(
                type.Namespace,
                "Beeexy.Domain.Sharing",
                StringComparison.Ordinal))
            .ToArray();
        var forbiddenFragments = new[]
        {
            "Endpoint",
            "Controller",
            "ScopeEvaluator",
            "Profile",
            "Renderer",
            "Storage",
            "Generator",
            "Hasher",
            "Jwt",
            "TokenIssuer",
            "Qr",
            "FhirMapper"
        };

        Assert.DoesNotContain(sharingTypes, type => forbiddenFragments.Any(fragment =>
            type.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void SharingFoundation_IntroducesNoApplicationUseCaseOrApiType()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var applicationAssembly = assemblies.SingleOrDefault(assembly =>
            assembly.GetName().Name == "Beeexy.Application") ??
            System.Reflection.Assembly.Load("Beeexy.Application");
        var apiAssembly = assemblies.SingleOrDefault(assembly =>
            assembly.GetName().Name == "Beeexy.Api") ??
            System.Reflection.Assembly.Load("Beeexy.Api");

        Assert.DoesNotContain(applicationAssembly.GetTypes(), type =>
            type.Namespace?.StartsWith("Beeexy.Application.Sharing", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(apiAssembly.GetTypes(), type =>
            type.Namespace?.StartsWith("Beeexy.Api.Sharing", StringComparison.Ordinal) == true);
    }
}
