using System.Security.Cryptography;
using Beeexy.Application.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Api.PrivateAccess;

internal static class PrivateAccessCli
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 ||
            !string.Equals(args[0], "private-access", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (args.Length == 2 &&
            string.Equals(
                args[1],
                "provision-demo-guest",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (args.Length >= 2 &&
            string.Equals(args[1], "generate", StringComparison.OrdinalIgnoreCase))
        {
            var count = args.Length >= 3 && int.TryParse(args[2], out var requestedCount)
                ? requestedCount
                : 5;
            foreach (var suggestion in PrivateAccessCredentialGenerator.Generate(count))
            {
                Console.WriteLine($"Username: {suggestion.Username}");
                Console.WriteLine($"Password: {suggestion.Password}");
                Console.WriteLine($"Keyword: {suggestion.Keyword}");
                Console.WriteLine();
            }

            return true;
        }

        if (args.Length >= 2 &&
            string.Equals(args[1], "hash", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write("Password: ");
            var password = ReadSecret();
            Console.Write("Keyword: ");
            var keyword = ReadSecret();
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(keyword))
            {
                throw new InvalidOperationException("Password and keyword must not be empty.");
            }

            Console.WriteLine(
                $"PrivateAccess__PasswordHash={PrivateAccessPasswordHasher.Hash(password)}");
            Console.WriteLine(
                $"PrivateAccess__KeywordHash={PrivateAccessPasswordHasher.Hash(keyword)}");
            Console.WriteLine(
                $"PrivateAccess__SessionSigningKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}");
            return true;
        }

        Console.WriteLine("Usage:");
        Console.WriteLine("  private-access generate [count]");
        Console.WriteLine("  private-access hash");
        Console.WriteLine("  private-access provision-demo-guest");
        return true;
    }

    public static bool IsProvisionDemoGuestCommand(string[] args) =>
        args.Length == 2 &&
        string.Equals(args[0], "private-access", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[1], "provision-demo-guest", StringComparison.OrdinalIgnoreCase);

    public static async Task ProvisionDemoGuestAsync(
        IServiceProvider services,
        PrivateAccessSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled ||
            !settings.DemoGuest.Enabled ||
            settings.DemoGuest.Definition is null)
        {
            throw new InvalidOperationException(
                "Private Access Demo Guest configuration is not enabled and complete.");
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<ProvisionDemoGuest>()
            .ExecuteAsync(settings.DemoGuest.Definition, cancellationToken);

        Console.WriteLine(result.WasProvisioned
            ? "Demo Guest account and primary profile provisioned."
            : "Existing Demo Guest account and primary profile verified.");
    }

    private static string ReadSecret()
    {
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(characters.ToArray());
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                {
                    characters.RemoveAt(characters.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                characters.Add(key.KeyChar);
            }
        }
    }
}
