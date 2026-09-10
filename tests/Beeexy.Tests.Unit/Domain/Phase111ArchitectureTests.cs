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
    [Trait("Category", "Phase112")]
    [Trait("Category", "Phase113")]
    [Trait("Category", "Phase114")]
    [Trait("Category", "Phase115")]
    public void SharingSurface_StopsAtPhase115LifecycleBoundary()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var applicationAssembly = assemblies.SingleOrDefault(assembly =>
            assembly.GetName().Name == "Beeexy.Application") ??
            System.Reflection.Assembly.Load("Beeexy.Application");
        var apiAssembly = assemblies.SingleOrDefault(assembly =>
            assembly.GetName().Name == "Beeexy.Api") ??
            System.Reflection.Assembly.Load("Beeexy.Api");

        Assert.Contains(applicationAssembly.GetTypes(), type =>
            type.FullName == "Beeexy.Application.Sharing.RevokeShare");
        Assert.Contains(applicationAssembly.GetTypes(), type =>
            type.FullName == "Beeexy.Application.Sharing.ExpireShares");
        Assert.Contains(applicationAssembly.GetTypes(), type =>
            type.FullName == "Beeexy.Application.Sharing.ListShareActivity");
        var forbidden = new[] { "Export", "Download", "Renderer", "Storage", "Qr" };
        Assert.DoesNotContain(applicationAssembly.GetTypes(), type =>
            type.Namespace?.StartsWith("Beeexy.Application.Sharing", StringComparison.Ordinal) == true &&
            forbidden.Any(fragment => type.Name.Contains(
                fragment,
                StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(apiAssembly.GetTypes(), type =>
            type.Namespace?.StartsWith("Beeexy.Api.Sharing", StringComparison.Ordinal) == true &&
            forbidden.Any(fragment => type.Name.Contains(
                fragment,
                StringComparison.OrdinalIgnoreCase)));
    }
}
