using Beeexy.Api.Configuration;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Configuration;

namespace Beeexy.Tests.Unit.Configuration;

public sealed class StartupConfigurationTests
{
    [Fact]
    public void ValidConfiguration_ReturnsDatabaseAndCorsSettings()
    {
        var configuration = BuildConfiguration(
            "Host=localhost;Database=beeexy;Username=beeexy;Password=local-only",
            "https://app.example");

        var connectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(configuration);
        var origins = StartupConfiguration.GetRequiredCorsAllowedOrigins(configuration);

        Assert.Contains("Database=beeexy", connectionString);
        Assert.Equal(["https://app.example"], origins);
    }

    [Fact]
    public void MissingDatabaseConnectionString_IsRejected()
    {
        var configuration = BuildConfiguration(string.Empty, "https://app.example");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredDatabaseConnectionString(configuration));

        Assert.Contains("BeeexyDatabase", exception.Message);
        Assert.DoesNotContain("Password", exception.Message);
    }

    [Fact]
    public void MissingCorsAllowedOrigins_IsRejected()
    {
        var configuration = BuildConfiguration(
            "Host=localhost;Database=beeexy;Username=beeexy;Password=local-only",
            null);

        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredCorsAllowedOrigins(configuration));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://app.example/")]
    [InlineData("https://user:secret@app.example")]
    [InlineData("https://app.example/path")]
    public void UnsafeCorsOrigin_IsRejected(string origin)
    {
        var configuration = BuildConfiguration(
            "Host=localhost;Database=beeexy;Username=beeexy;Password=local-only",
            origin);

        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredCorsAllowedOrigins(configuration));
    }

    [Fact]
    public void SchedulerAssignments_ParseMultipleExplicitClinicScopes()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Scheduling:AppointmentSchedulers:Assignments:0:AccountId"] =
                    accountA.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:0:ClinicIds:0"] =
                    clinicA.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:0:ClinicIds:1"] =
                    clinicB.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:1:AccountId"] =
                    accountB.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:1:ClinicIds:0"] =
                    clinicA.ToString("D")
            }).Build();

        var assignments = StartupConfiguration.GetAppointmentSchedulerAssignments(configuration);

        Assert.True(assignments.HasAppointmentSchedulerPermission(
            EntityId.From(accountA), EntityId.From(clinicA)));
        Assert.True(assignments.HasAppointmentSchedulerPermission(
            EntityId.From(accountA), EntityId.From(clinicB)));
        Assert.True(assignments.HasAppointmentSchedulerPermission(
            EntityId.From(accountB), EntityId.From(clinicA)));
        Assert.False(assignments.HasAppointmentSchedulerPermission(
            EntityId.From(accountB), EntityId.From(clinicB)));
    }

    [Fact]
    public void MissingSchedulerConfiguration_GrantsNothing()
    {
        var assignments = StartupConfiguration.GetAppointmentSchedulerAssignments(
            new ConfigurationBuilder().Build());

        Assert.False(assignments.HasAppointmentSchedulerPermission(
            EntityId.New(), EntityId.New()));
    }

    [Theory]
    [InlineData("not-an-account", "6e37904d-a873-4904-b55a-27adbfa6e710")]
    [InlineData("6e37904d-a873-4904-b55a-27adbfa6e710", "not-a-clinic")]
    [InlineData("00000000-0000-0000-0000-000000000000", "6e37904d-a873-4904-b55a-27adbfa6e710")]
    public void MalformedSchedulerConfiguration_IsRejectedFailClosed(
        string accountId,
        string clinicId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Scheduling:AppointmentSchedulers:Assignments:0:AccountId"] = accountId,
                ["Scheduling:AppointmentSchedulers:Assignments:0:ClinicIds:0"] = clinicId
            }).Build();

        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetAppointmentSchedulerAssignments(configuration));
    }

    private static IConfiguration BuildConfiguration(string connectionString, string? origin)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:BeeexyDatabase"] = connectionString
        };

        if (origin is not null)
        {
            values["Cors:AllowedOrigins:0"] = origin;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
