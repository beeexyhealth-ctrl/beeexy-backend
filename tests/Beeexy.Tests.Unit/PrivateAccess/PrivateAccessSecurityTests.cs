using Beeexy.Api.PrivateAccess;
using Microsoft.AspNetCore.Http;

namespace Beeexy.Tests.Unit.PrivateAccess;

public sealed class PrivateAccessSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PasswordHash_RoundTripsAndRejectsWrongSecret()
    {
        var encoded = PrivateAccessPasswordHasher.Hash("StrongPassword!123");

        Assert.True(PrivateAccessPasswordHasher.IsValidEncodedHash(encoded));
        Assert.True(PrivateAccessPasswordHasher.Verify("StrongPassword!123", encoded));
        Assert.False(PrivateAccessPasswordHasher.Verify("wrong", encoded));
        Assert.DoesNotContain("StrongPassword!123", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionToken_RecognizesValidAndRejectsExpiredOrTamperedTokens()
    {
        var service = new PrivateAccessSessionTokenService(Settings());
        var session = service.Issue(Now);

        Assert.True(service.TryValidate(session.Token, Now, out var expiresAt));
        Assert.Equal(Now.AddMinutes(30), expiresAt);
        Assert.False(service.TryValidate(session.Token, expiresAt, out _));

        var replacement = session.Token[^1] == 'A' ? 'B' : 'A';
        var tampered = session.Token[..^1] + replacement;
        Assert.False(service.TryValidate(tampered, Now, out _));
    }

    [Fact]
    public void Generator_ProducesUniqueStrongThematicSuggestions()
    {
        var suggestions = PrivateAccessCredentialGenerator.Generate(50);

        Assert.Equal(50, suggestions.Count);
        Assert.Equal(50, suggestions.Select(item => item.Password).Distinct().Count());
        foreach (var suggestion in suggestions)
        {
            Assert.Contains(
                PrivateAccessCredentialGenerator.Brands,
                brand => suggestion.Username.StartsWith(brand, StringComparison.Ordinal));
            Assert.Contains(
                PrivateAccessCredentialGenerator.HealthWords,
                word => suggestion.Username.Contains(word, StringComparison.Ordinal));
            Assert.Contains(
                PrivateAccessCredentialGenerator.HealthWords,
                word => suggestion.Keyword.Contains(word, StringComparison.Ordinal));
            Assert.Contains(
                PrivateAccessCredentialGenerator.TechnologyWords,
                word => suggestion.Keyword.Contains(word, StringComparison.Ordinal));
            Assert.True(suggestion.Password.Length >= 24);
            Assert.Contains(suggestion.Password, char.IsUpper);
            Assert.Contains(suggestion.Password, char.IsLower);
            Assert.Contains(suggestion.Password, char.IsDigit);
            Assert.Contains(suggestion.Password, character => "!#%+@".Contains(character));
        }
    }

    [Fact]
    public void ProductionCookie_IsSecureHttpOnlyAndCrossSiteCompatible()
    {
        var options = PrivateAccessEndpointExtensions.CreateCookieOptions(
            Settings(),
            Now.AddMinutes(30));

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.None, options.SameSite);
        Assert.Equal("/", options.Path);
        Assert.Equal(Now.AddMinutes(30), options.Expires);
    }

    private static PrivateAccessSettings Settings() => new(
        true,
        "BeeexyHealth",
        PrivateAccessPasswordHasher.Hash("StrongPassword!123"),
        PrivateAccessPasswordHasher.Hash("HealthTech"),
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
        TimeSpan.FromMinutes(30),
        5,
        TimeSpan.FromMinutes(15),
        true);
}
