using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Api.PrivateAccess;

internal static partial class PrivateAccessCli
{
    private static readonly DateOnly SyntheticDateOfBirth = new(1990, 1, 1);
    private static readonly UsState SyntheticState = UsState.Create("CA");
    private static readonly UserTimeZone SyntheticTimeZone = UserTimeZone.Create("America/Lima");
    private const string SecretAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%+@";
    private const string UsernameAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "private-access")
        {
            return false;
        }

        if (IsDatabaseCommand(args) || IsProvisionDemoGuestCommand(args))
        {
            return false;
        }

        Console.WriteLine("Usage:");
        Console.WriteLine("  private-access provision-testers --batch-id <slug> --count <1..100> --output <file.csv>");
        Console.WriteLine("  private-access migrate-demo-guest");
        Console.WriteLine("  private-access list [--batch-id <slug>]");
        Console.WriteLine("  private-access deactivate|activate --tester-key <key>");
        Console.WriteLine("  private-access revoke --tester-key <key> --confirm");
        Console.WriteLine("  private-access rotate-credentials --tester-key <key> --output <file.csv>");
        Console.WriteLine("  private-access provision-demo-guest  (legacy compatibility only)");
        return true;
    }

    public static bool IsDatabaseCommand(string[] args) =>
        args.Length >= 2 && args[0] == "private-access" && args[1] is
            "provision-testers" or "migrate-demo-guest" or "list" or
            "deactivate" or "activate" or "revoke" or "rotate-credentials";

    public static bool IsProvisionDemoGuestCommand(string[] args) =>
        args.Length == 2 && args[0] == "private-access" && args[1] == "provision-demo-guest";

    public static async Task RunDatabaseCommandAsync(
        string[] args,
        IServiceProvider services,
        PrivateAccessSettings? legacySettings,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BeeexyDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPrivateAccessSecretHasher>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IPrivateAccessAuditLogger>();
        switch (args[1])
        {
            case "provision-testers":
                await ProvisionTestersAsync(args, db, hasher, auditLogger, cancellationToken);
                break;
            case "migrate-demo-guest":
                await MigrateDemoGuestAsync(db, legacySettings, auditLogger, cancellationToken);
                break;
            case "list":
                await ListAsync(args, db, cancellationToken);
                break;
            case "deactivate":
            case "activate":
            case "revoke":
                await ChangeStatusAsync(args, db, auditLogger, cancellationToken);
                break;
            case "rotate-credentials":
                await RotateCredentialsAsync(args, db, hasher, auditLogger, cancellationToken);
                break;
        }
    }

    public static async Task ProvisionDemoGuestAsync(
        IServiceProvider services,
        PrivateAccessSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled || !settings.DemoGuest.Enabled || settings.DemoGuest.Definition is null)
        {
            throw new InvalidOperationException(
                "Private Access Demo Guest configuration is not enabled and complete.");
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ProvisionDemoGuest>()
            .ExecuteAsync(settings.DemoGuest.Definition, cancellationToken);
        Console.WriteLine(result.WasProvisioned
            ? "Demo Guest account and primary profile provisioned."
            : "Existing Demo Guest account and primary profile verified.");
    }

    private static async Task ProvisionTestersAsync(
        string[] args,
        BeeexyDbContext db,
        IPrivateAccessSecretHasher hasher,
        IPrivateAccessAuditLogger auditLogger,
        CancellationToken cancellationToken)
    {
        var batchId = RequiredOption(args, "--batch-id");
        var output = RequiredOption(args, "--output");
        if (!BatchIdPattern().IsMatch(batchId))
        {
            throw new InvalidOperationException(
                "Batch ID must contain 1-40 lowercase letters, digits, or hyphens.");
        }

        if (!int.TryParse(RequiredOption(args, "--count"), out var count) || count is < 1 or > 100)
        {
            throw new InvalidOperationException("Count must be between 1 and 100.");
        }

        var prefix = batchId + "-tester-";
        var existing = await db.PrivateAccessCredentials
            .Where(value => value.TesterKey.StartsWith(prefix))
            .OrderBy(value => value.TesterKey)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            if (existing.Count != count)
            {
                throw new InvalidOperationException("The existing batch is partial or has a different count.");
            }

            await VerifyExistingBatchAsync(existing, db, cancellationToken);
            Console.WriteLine($"Verified {existing.Count} existing testers. No plaintext credentials were emitted.");
            return;
        }

        EnsureNewOutputPath(output);

        var now = DateTimeOffset.UtcNow;
        var generated = Enumerable.Range(1, count)
            .Select(ordinal => GenerateTester(batchId, ordinal, hasher, now))
            .ToArray();
        var temporaryPath = CreateTemporaryOutputPath(output);
        var committed = false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in generated)
            {
                db.AddRange(item.Account, item.Profile, item.Preference, item.Credential);
            }

            await db.SaveChangesAsync(cancellationToken);
            await WriteCsvAsync(temporaryPath, generated, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            File.Move(temporaryPath, Path.GetFullPath(output));
            foreach (var item in generated)
                auditLogger.CredentialChanged(item.Credential.Id, item.Account.Id, "provisioned");
            Console.WriteLine($"Provisioned {count} testers. One-time credentials: {Path.GetFullPath(output)}");
            Console.WriteLine("Transfer the file through an approved encrypted channel and remove local copies afterward.");
        }
        catch (Exception exception)
        {
            if (!committed && File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (committed && File.Exists(temporaryPath))
                throw new InvalidOperationException(
                    $"The database committed, but finalizing the credential artifact failed. Recover it from {temporaryPath}.",
                    exception);
            throw;
        }
    }

    private static GeneratedTester GenerateTester(
        string batchId,
        int ordinal,
        IPrivateAccessSecretHasher hasher,
        DateTimeOffset now)
    {
        var testerKey = $"{batchId}-tester-{ordinal:000}";
        var username = $"{testerKey}-{RandomUsernameText(8)}";
        var password = RandomText(24);
        var keyword = RandomText(24);
        var account = Account.Create(NormalizedEmail.Create($"{testerKey}@demo.beeexy.invalid"), now);
        var profileId = EntityId.New();
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{profileId.Value:N}".ToUpperInvariant()), now, account.Id, profileId);
        profile.UpdateDemographics(
            PatientName.Create("Demo"),
            PatientName.Create($"Tester {ordinal:000}"),
            SyntheticDateOfBirth,
            ordinal % 2 == 1 ? SexAssignedAtBirth.Female : SexAssignedAtBirth.Male,
            SyntheticState,
            now);
        var preference = UserPreference.Create(account.Id, SyntheticTimeZone, now);
        var credential = PrivateAccessCredential.Create(
            account.Id, testerKey, username, hasher.Hash(password), hasher.Hash(keyword), now);
        return new GeneratedTester(account, profile, preference, credential, password, keyword);
    }

    private static async Task VerifyExistingBatchAsync(
        IReadOnlyList<PrivateAccessCredential> credentials,
        BeeexyDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (var credential in credentials)
        {
            var accountCount = await db.Accounts.CountAsync(value => value.Id == credential.AccountId, cancellationToken);
            var profileCount = await db.PatientProfiles.CountAsync(value => value.AccountId == credential.AccountId, cancellationToken);
            var preferenceCount = await db.UserPreferences.CountAsync(value => value.AccountId == credential.AccountId, cancellationToken);
            if (accountCount != 1 || profileCount != 1 || preferenceCount != 1)
            {
                throw new InvalidOperationException(
                    $"Tester {credential.TesterKey} has an incompatible account relationship.");
            }
        }
    }

    private static async Task MigrateDemoGuestAsync(
        BeeexyDbContext db,
        PrivateAccessSettings? settings,
        IPrivateAccessAuditLogger auditLogger,
        CancellationToken cancellationToken)
    {
        if (settings is not { Enabled: true, AuthenticationMode: PrivateAccessAuthenticationMode.Legacy } ||
            settings.DemoGuest.Definition is null || string.IsNullOrWhiteSpace(settings.Username) ||
            string.IsNullOrWhiteSpace(settings.PasswordHash) || string.IsNullOrWhiteSpace(settings.KeywordHash))
        {
            throw new InvalidOperationException("Complete legacy Private Access and Demo Guest configuration is required.");
        }

        var definition = settings.DemoGuest.Definition;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(
            value => value.Email == definition.Email, cancellationToken)
            ?? throw new InvalidOperationException("The legacy Demo Guest account is missing.");
        var profiles = await db.PatientProfiles.Where(value => value.AccountId == account.Id).ToListAsync(cancellationToken);
        var preferences = await db.UserPreferences.Where(value => value.AccountId == account.Id).ToListAsync(cancellationToken);
        if (DemoGuestAccountResolver.TryResolve(
                definition,
                new DemoGuestAccountState(account, profiles, preferences)) is null)
        {
            throw new InvalidOperationException("The legacy Demo Guest identity is incompatible.");
        }

        var existing = await db.PrivateAccessCredentials.SingleOrDefaultAsync(
            value => value.TesterKey == "legacy-demo-guest", cancellationToken);
        if (existing is null)
        {
            db.PrivateAccessCredentials.Add(PrivateAccessCredential.Create(
                account.Id, "legacy-demo-guest", settings.Username,
                settings.PasswordHash, settings.KeywordHash, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (existing.AccountId != account.Id || existing.Username != settings.Username)
        {
            throw new InvalidOperationException("The migrated legacy credential conflicts with configuration.");
        }

        await transaction.CommitAsync(cancellationToken);
        var migrated = existing ?? await db.PrivateAccessCredentials.SingleAsync(
            value => value.TesterKey == "legacy-demo-guest", cancellationToken);
        auditLogger.CredentialChanged(migrated.Id, account.Id, "legacy_migrated");
        Console.WriteLine(existing is null
            ? "Migrated the legacy Demo Guest credential and preserved its account and clinical data."
            : "Verified the existing legacy Demo Guest migration.");
    }

    private static async Task ListAsync(string[] args, BeeexyDbContext db, CancellationToken cancellationToken)
    {
        var batch = OptionalOption(args, "--batch-id");
        var query = db.PrivateAccessCredentials.AsNoTracking();
        if (batch is not null) query = query.Where(value => value.TesterKey.StartsWith(batch + "-tester-"));
        var values = await query.OrderBy(value => value.TesterKey).ToListAsync(cancellationToken);
        foreach (var value in values)
        {
            Console.WriteLine($"{value.TesterKey}\t{value.Username}\t{value.Status.ToString().ToLowerInvariant()}\t{value.AccountId.Value:D}");
        }
    }

    private static async Task ChangeStatusAsync(
        string[] args,
        BeeexyDbContext db,
        IPrivateAccessAuditLogger auditLogger,
        CancellationToken cancellationToken)
    {
        var testerKey = RequiredOption(args, "--tester-key");
        if (args[1] == "revoke" && !args.Contains("--confirm", StringComparer.Ordinal))
            throw new InvalidOperationException("Permanent revocation requires --confirm.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var credential = await db.PrivateAccessCredentials.SingleOrDefaultAsync(
            value => value.TesterKey == testerKey, cancellationToken)
            ?? throw new InvalidOperationException("Tester credential not found.");
        var account = await db.Accounts.SingleAsync(value => value.Id == credential.AccountId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        switch (args[1])
        {
            case "activate": credential.Activate(now); account.Activate(now); break;
            case "deactivate": credential.Disable(now); account.Disable(now); await RevokeAllSessionsAsync(db, credential, now, cancellationToken); break;
            case "revoke": credential.Revoke(now); account.Disable(now); await RevokeAllSessionsAsync(db, credential, now, cancellationToken); break;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        auditLogger.CredentialChanged(credential.Id, account.Id, args[1]);
        Console.WriteLine($"{args[1]} completed for {testerKey}.");
    }

    private static async Task RotateCredentialsAsync(
        string[] args,
        BeeexyDbContext db,
        IPrivateAccessSecretHasher hasher,
        IPrivateAccessAuditLogger auditLogger,
        CancellationToken cancellationToken)
    {
        var testerKey = RequiredOption(args, "--tester-key");
        var output = RequiredOption(args, "--output");
        EnsureNewOutputPath(output);
        var password = RandomText(24);
        var keyword = RandomText(24);
        var temporaryPath = CreateTemporaryOutputPath(output);
        var committed = false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var credential = await db.PrivateAccessCredentials.SingleOrDefaultAsync(
                value => value.TesterKey == testerKey, cancellationToken)
                ?? throw new InvalidOperationException("Tester credential not found.");
            var account = await db.Accounts.SingleAsync(value => value.Id == credential.AccountId, cancellationToken);
            var profile = await db.PatientProfiles.SingleAsync(value => value.AccountId == account.Id, cancellationToken);
            var preference = await db.UserPreferences.SingleAsync(value => value.AccountId == account.Id, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            credential.RotateSecrets(hasher.Hash(password), hasher.Hash(keyword), now);
            await RevokeAllSessionsAsync(db, credential, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await WriteCsvAsync(temporaryPath,
                [new GeneratedTester(account, profile, preference, credential, password, keyword)], cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            File.Move(temporaryPath, Path.GetFullPath(output));
            auditLogger.CredentialChanged(credential.Id, account.Id, "credentials_rotated");
            Console.WriteLine($"Rotated credentials for {testerKey}. One-time credentials: {Path.GetFullPath(output)}");
        }
        catch (Exception exception)
        {
            if (!committed && File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (committed && File.Exists(temporaryPath))
                throw new InvalidOperationException(
                    $"The database committed, but finalizing the credential artifact failed. Recover it from {temporaryPath}.",
                    exception);
            throw;
        }
    }

    private static async Task RevokeAllSessionsAsync(
        BeeexyDbContext db,
        PrivateAccessCredential credential,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.PrivateAccessSessions
            .Where(value => value.CredentialId == credential.Id && value.Status == PrivateAccessSessionStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, PrivateAccessSessionStatus.Revoked)
                .SetProperty(value => value.RevokedAt, now)
                .SetProperty(value => value.UpdatedAt, now), cancellationToken);
        await db.RefreshSessions
            .Where(value => value.AccountId == credential.AccountId && value.Status == RefreshSessionStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, RefreshSessionStatus.Revoked)
                .SetProperty(value => value.RevokedAt, now)
                .SetProperty(value => value.UpdatedAt, now), cancellationToken);
    }

    private static async Task WriteCsvAsync(
        string path,
        IReadOnlyList<GeneratedTester> values,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var stream = new FileStream(path, options);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("tester_key,username,password,keyword,account_id,profile_id,beeexy_id");
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', value.Credential.TesterKey, value.Credential.Username,
                value.Password, value.Keyword, value.Account.Id.Value.ToString("D"),
                value.Profile.Id.Value.ToString("D"), value.Profile.BeeexyId.Value));
        }
    }

    private static string RandomText(int length) => string.Create(length, 0, static (buffer, _) =>
    {
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = SecretAlphabet[RandomNumberGenerator.GetInt32(SecretAlphabet.Length)];
    });

    private static string RandomUsernameText(int length) => string.Create(length, 0, static (buffer, _) =>
    {
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = UsernameAlphabet[RandomNumberGenerator.GetInt32(UsernameAlphabet.Length)];
    });

    private static string RequiredOption(string[] args, string name) =>
        OptionalOption(args, name) ?? throw new InvalidOperationException($"Missing required option {name}.");

    private static string? OptionalOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void EnsureNewOutputPath(string output)
    {
        var fullPath = Path.GetFullPath(output);
        if (File.Exists(fullPath)) throw new InvalidOperationException("The output file already exists and will not be overwritten.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    }

    private static string CreateTemporaryOutputPath(string output) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,
            $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex BatchIdPattern();

    private sealed record GeneratedTester(
        Account Account,
        PatientProfile Profile,
        UserPreference Preference,
        PrivateAccessCredential Credential,
        string Password,
        string Keyword);
}
