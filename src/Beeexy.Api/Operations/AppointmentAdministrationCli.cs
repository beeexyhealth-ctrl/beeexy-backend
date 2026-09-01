using Beeexy.Api.Configuration;
using Beeexy.Application.Common;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Beeexy.Api.Operations;

internal static class AppointmentAdministrationCli
{
    public const string ListCommand = "appointment-list-requested";
    public const string ConfirmCommand = "appointment-confirm";
    public const string RejectCommand = "appointment-reject";
    public const int SuccessExitCode = 0;
    public const int InvalidArgumentsExitCode = 1;
    public const int NotFoundExitCode = 2;
    public const int ConflictExitCode = 3;
    public const int ConfigurationExitCode = 4;
    public const int UnexpectedFailureExitCode = 5;

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0] is ListCommand or ConfirmCommand or RejectCommand;

    public static async Task<int> ExecuteAsync(
        string[] args,
        IConfiguration configuration,
        string? environmentName,
        TextReader? input = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        input ??= Console.In;
        output ??= Console.Out;
        error ??= Console.Error;

        if (!IsAllowedEnvironment(environmentName))
        {
            await error.WriteLineAsync(
                "ASPNETCORE_ENVIRONMENT must be explicitly set to Development or Production.");
            return ConfigurationExitCode;
        }

        CommandOptions command;
        try
        {
            command = Parse(args);
        }
        catch (CliUsageException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return InvalidArgumentsExitCode;
        }

        string connectionString;
        try
        {
            connectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(
                configuration);
            _ = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            await error.WriteLineAsync(exception is ArgumentException
                ? "ConnectionStrings:BeeexyDatabase is invalid."
                : exception.Message);
            return ConfigurationExitCode;
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        }).AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
        services.AddAppointmentOperationsInfrastructure(connectionString);
        services.AddScoped<AppointmentTransitionEngine>();
        services.AddScoped<ListRequestedAppointmentsForOperations>();
        services.AddScoped<GetAppointmentForOperations>();
        services.AddScoped<ConfirmAppointmentForOperations>();
        services.AddScoped<RejectAppointmentForOperations>();

        await using var provider = services.BuildServiceProvider();
        try
        {
            if (command is MutationOptions)
            {
                await output.WriteLineAsync($"Environment: {environmentName}");
            }

            return await RunAsync(
                command,
                provider,
                input,
                output,
                cancellationToken);
        }
        catch (AppointmentNotFoundException)
        {
            await error.WriteLineAsync("Appointment not found.");
            return NotFoundExitCode;
        }
        catch (Exception exception) when (exception is AppointmentTransitionConflictException or
            AppointmentTransitionConcurrencyException or AppointmentSlotReservationConflictException)
        {
            await error.WriteLineAsync(
                "Conflict: the appointment changed or cannot apply this transition. " +
                "List or inspect its current state, then rerun the command.");
            return ConflictExitCode;
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return InvalidArgumentsExitCode;
        }
        catch (Exception exception)
        {
            var logger = provider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Beeexy.Operations.AppointmentAdministration");
            logger.LogError(
                "Appointment operational command {Command} failed unexpectedly with " +
                "failure type {FailureType}.",
                command.Name,
                exception.GetType().Name);
            await error.WriteLineAsync(
                "The appointment command failed unexpectedly. Review the structured server log.");
            return UnexpectedFailureExitCode;
        }
    }

    internal static bool IsAllowedEnvironment(string? environmentName) =>
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);

    internal static CommandOptions Parse(string[] args)
    {
        if (!IsCommand(args))
        {
            throw Usage("Unknown appointment administration command.");
        }

        return args[0] switch
        {
            ListCommand => ParseList(args),
            ConfirmCommand => ParseMutation(args, reject: false),
            RejectCommand => ParseMutation(args, reject: true),
            _ => throw Usage("Unknown appointment administration command.")
        };
    }

    private static async Task<int> RunAsync(
        CommandOptions command,
        IServiceProvider provider,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Beeexy.Operations.AppointmentAdministration");
        if (command is ListOptions list)
        {
            var items = await scope.ServiceProvider
                .GetRequiredService<ListRequestedAppointmentsForOperations>()
                .ExecuteAsync(EntityId.From(list.ClinicId), list.Limit, cancellationToken);
            logger.LogInformation(
                "Appointment operational command {Command} listed {ResultCount} requested " +
                "appointments for clinic {ClinicId}.",
                ListCommand,
                items.Count,
                list.ClinicId);
            await WriteListAsync(output, list.ClinicId, items);
            return SuccessExitCode;
        }

        var mutation = (MutationOptions)command;
        var appointmentId = EntityId.From(mutation.AppointmentId);
        var current = await scope.ServiceProvider
            .GetRequiredService<GetAppointmentForOperations>()
            .ExecuteAsync(appointmentId, cancellationToken);
        if (mutation.Reject && !mutation.Yes)
        {
            await WriteSchedulingSummaryAsync(output, current);
            await output.WriteAsync("Reject this appointment? [y/N] ");
            var response = await input.ReadLineAsync(cancellationToken);
            if (!string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                await output.WriteLineAsync("Result: cancelled; no changes made.");
                return SuccessExitCode;
            }
        }

        var result = mutation.Reject
            ? await scope.ServiceProvider.GetRequiredService<RejectAppointmentForOperations>()
                .ExecuteAsync(appointmentId, mutation.Actor, cancellationToken)
            : await scope.ServiceProvider.GetRequiredService<ConfirmAppointmentForOperations>()
                .ExecuteAsync(appointmentId, mutation.Actor, cancellationToken);
        logger.LogInformation(
            "Appointment operational command {Command} completed for appointment " +
            "{AppointmentId}, clinic {ClinicId}, actor {OperationalActor}, status " +
            "{ResultStatus}, newly applied {NewlyApplied}.",
            mutation.Name,
            mutation.AppointmentId,
            result.Appointment.ClinicId.Value,
            mutation.Actor,
            result.Appointment.Status,
            result.NewlyApplied);
        await output.WriteLineAsync($"Appointment: {mutation.AppointmentId:D}");
        await output.WriteLineAsync($"Previous status: {current.Status}");
        await output.WriteLineAsync($"Current status: {result.Appointment.Status}");
        await output.WriteLineAsync(
            result.NewlyApplied ? "Result: success" : "Result: success (already applied)");
        return SuccessExitCode;
    }

    internal static Task WriteListAsync(
        TextWriter output,
        Guid clinicId,
        IReadOnlyList<OperationalAppointmentSummary> items)
    {
        if (items.Count == 0)
        {
            return output.WriteLineAsync(
                $"No requested appointments found for clinic {clinicId:D}.");
        }

        var lines = new List<string>
        {
            $"Requested appointments for clinic {clinicId:D}",
            string.Empty,
            "AppointmentId | ClinicId | Doctor | StartsAt | EndsAt | ClinicTimeZone | " +
            "Modality | Status | CreatedAt"
        };
        lines.AddRange(items.Select(item => string.Join(" | ",
            item.AppointmentId.Value.ToString("D"),
            item.ClinicId.Value.ToString("D"),
            SafeText(item.Doctor),
            item.StartsAt.ToUniversalTime().ToString("O"),
            item.EndsAt.ToUniversalTime().ToString("O"),
            item.ClinicTimeZone,
            item.Modality,
            item.Status,
            item.CreatedAt.ToUniversalTime().ToString("O"))));
        return output.WriteLineAsync(string.Join(Environment.NewLine, lines));
    }

    private static async Task WriteSchedulingSummaryAsync(
        TextWriter output,
        OperationalAppointmentSummary appointment)
    {
        await output.WriteLineAsync($"Appointment: {appointment.AppointmentId.Value:D}");
        await output.WriteLineAsync($"Clinic: {appointment.ClinicId.Value:D}");
        await output.WriteLineAsync($"Doctor: {SafeText(appointment.Doctor)}");
        await output.WriteLineAsync(
            $"StartsAt: {appointment.StartsAt.ToUniversalTime():O}");
        await output.WriteLineAsync($"Modality: {appointment.Modality}");
        await output.WriteLineAsync($"Status: {appointment.Status}");
    }

    private static ListOptions ParseList(string[] args)
    {
        Guid? clinicId = null;
        var limit = ListRequestedAppointmentsForOperations.DefaultLimit;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--clinic" when clinicId is null:
                    clinicId = ParseGuid(Next(args, ref index, "--clinic"), "clinic");
                    break;
                case "--limit":
                    if (!int.TryParse(Next(args, ref index, "--limit"), out limit) ||
                        limit is < 1 or > ListRequestedAppointmentsForOperations.MaximumLimit)
                    {
                        throw Usage(
                            $"--limit must be between 1 and " +
                            $"{ListRequestedAppointmentsForOperations.MaximumLimit}.");
                    }
                    break;
                default:
                    throw Usage($"Unsupported or duplicate argument '{args[index]}'.");
            }
        }

        return new ListOptions(clinicId ?? throw Usage("--clinic is required."), limit);
    }

    private static MutationOptions ParseMutation(string[] args, bool reject)
    {
        if (args.Length < 2)
        {
            throw Usage("An appointment identifier is required.");
        }

        var appointmentId = ParseGuid(args[1], "appointment");
        string? actor = null;
        var yes = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--actor" when actor is null:
                    actor = Next(args, ref index, "--actor").Trim();
                    break;
                case "--yes" when reject && !yes:
                    yes = true;
                    break;
                default:
                    throw Usage($"Unsupported or duplicate argument '{args[index]}'.");
            }
        }

        try
        {
            actor = AppointmentActor.BeeexyOperations(actor ?? string.Empty)
                .OperationalIdentifier;
        }
        catch (ArgumentException exception)
        {
            throw Usage(exception.Message);
        }

        return new MutationOptions(
            reject ? RejectCommand : ConfirmCommand,
            appointmentId,
            actor!,
            reject,
            yes);
    }

    private static Guid ParseGuid(string value, string name) =>
        Guid.TryParseExact(value, "D", out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw Usage($"The {name} identifier must be a non-empty UUID.");

    private static string SafeText(string value) => new(
        value.Select(character => char.IsControl(character) || character == '|'
            ? ' '
            : character).ToArray());

    private static string Next(string[] args, ref int index, string option)
    {
        index++;
        if (index >= args.Length)
        {
            throw Usage($"{option} requires a value.");
        }

        return args[index];
    }

    private static CliUsageException Usage(string message) => new(
        $"{message}{Environment.NewLine}" +
        $"Usage: {ListCommand} --clinic <clinicId> [--limit <1-200>]{Environment.NewLine}" +
        $"       {ConfirmCommand} <appointmentId> --actor <operatorIdentifier>" +
        $"{Environment.NewLine}" +
        $"       {RejectCommand} <appointmentId> --actor <operatorIdentifier> [--yes]");

    internal abstract record CommandOptions(string Name);

    internal sealed record ListOptions(Guid ClinicId, int Limit)
        : CommandOptions(ListCommand);

    internal sealed record MutationOptions(
        string CommandName,
        Guid AppointmentId,
        string Actor,
        bool Reject,
        bool Yes) : CommandOptions(CommandName);

    private sealed class CliUsageException(string message) : Exception(message);
}
