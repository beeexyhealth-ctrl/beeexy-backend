using Beeexy.Application.Identity;
using Beeexy.Infrastructure.Identity;

namespace Beeexy.Tests.Unit.Identity;

public sealed class GoogleExternalIdentityProviderTests
{
    private const string ClientId = "unit-test.apps.googleusercontent.com";

    [Fact]
    public async Task ValidToken_ReturnsSubjectAndNormalizedVerifiedEmail()
    {
        var validator = new StubValidator(new GoogleIdTokenPayload(
            " google-subject ",
            "Person@Example.com",
            true));
        var provider = CreateProvider(validator);

        var result = await provider.ValidateAsync("signed-google-id-token");

        Assert.Equal("google", result.Provider);
        Assert.Equal("google-subject", result.Subject);
        Assert.Equal("person@example.com", result.VerifiedEmail?.Value);
        Assert.Equal(ClientId, validator.ObservedClientId);
    }

    [Fact]
    public async Task UnverifiedEmail_IsNotExposedAsTrustedEmail()
    {
        var provider = CreateProvider(new StubValidator(new GoogleIdTokenPayload(
            "google-subject",
            "person@example.com",
            false)));

        var result = await provider.ValidateAsync("signed-google-id-token");

        Assert.Null(result.VerifiedEmail);
    }

    [Theory]
    [InlineData("invalid-signature")]
    [InlineData("expired")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("malformed")]
    public async Task RejectedGoogleToken_ReturnsGenericAuthenticationFailure(string scenario)
    {
        var provider = CreateProvider(new StubValidator(new GoogleIdTokenRejectedException()));

        var exception = await Assert.ThrowsAsync<ExternalIdentityAuthenticationException>(
            () => provider.ValidateAsync(scenario));

        Assert.DoesNotContain(scenario, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderInfrastructureFailure_ReturnsUnavailable()
    {
        var provider = CreateProvider(
            new StubValidator(new GoogleIdTokenProviderUnavailableException()));

        await Assert.ThrowsAsync<ExternalIdentityProviderUnavailableException>(
            () => provider.ValidateAsync("signed-google-id-token"));
    }

    [Fact]
    public async Task DisabledProvider_ReturnsUnavailableWithoutCallingValidator()
    {
        var validator = new StubValidator(new GoogleIdTokenPayload(
            "subject",
            "person@example.com",
            true));
        var provider = new GoogleExternalIdentityProvider(
            new GoogleExternalIdentityOptions(false, null),
            validator);

        await Assert.ThrowsAsync<ExternalIdentityProviderUnavailableException>(
            () => provider.ValidateAsync("signed-google-id-token"));
        Assert.Null(validator.ObservedClientId);
    }

    [Theory]
    [InlineData(null, "person@example.com", true)]
    [InlineData("", "person@example.com", true)]
    [InlineData("subject", "not-an-email", true)]
    public async Task InvalidTrustedPayload_ReturnsAuthenticationFailure(
        string? subject,
        string? email,
        bool emailVerified)
    {
        var provider = CreateProvider(new StubValidator(new GoogleIdTokenPayload(
            subject,
            email,
            emailVerified)));

        await Assert.ThrowsAsync<ExternalIdentityAuthenticationException>(
            () => provider.ValidateAsync("signed-google-id-token"));
    }

    private static GoogleExternalIdentityProvider CreateProvider(IGoogleIdTokenValidator validator)
    {
        return new GoogleExternalIdentityProvider(
            new GoogleExternalIdentityOptions(true, ClientId),
            validator);
    }

    private sealed class StubValidator : IGoogleIdTokenValidator
    {
        private readonly GoogleIdTokenPayload? _payload;
        private readonly Exception? _exception;

        public StubValidator(GoogleIdTokenPayload payload)
        {
            _payload = payload;
        }

        public StubValidator(Exception exception)
        {
            _exception = exception;
        }

        public string? ObservedClientId { get; private set; }

        public Task<GoogleIdTokenPayload> ValidateAsync(
            string credential,
            string clientId,
            CancellationToken cancellationToken = default)
        {
            ObservedClientId = clientId;
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_payload!);
        }
    }
}
