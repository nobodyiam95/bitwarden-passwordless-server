using System.Net;
using System.Net.Http.Json;
using Bogus;
using Fido2NetLib;
using FluentAssertions;
using Passwordless.Api.Endpoints;
using Passwordless.Api.IntegrationTests.Helpers;
using Passwordless.Api.IntegrationTests.Helpers.User;
using Passwordless.Service.Models;
using Xunit;

namespace Passwordless.Api.IntegrationTests.Endpoints.Register;

public class RegisterTests : IClassFixture<PasswordlessApiFactory>, IDisposable
{
    private readonly HttpClient _client;

    private static readonly Faker<RegisterToken> TokenGenerator = new Faker<RegisterToken>()
        .RuleFor(x => x.UserId, Guid.NewGuid().ToString())
        .RuleFor(x => x.DisplayName, x => x.Person.FullName)
        .RuleFor(x => x.Username, x => x.Person.Email)
        .RuleFor(x => x.Attestation, "None")
        .RuleFor(x => x.Discoverable, true)
        .RuleFor(x => x.UserVerification, "Preferred")
        .RuleFor(x => x.Aliases, x => new HashSet<string> { x.Person.FirstName })
        .RuleFor(x => x.AliasHashing, false)
        .RuleFor(x => x.ExpiresAt, DateTime.UtcNow.AddDays(1))
        .RuleFor(x => x.TokenId, Guid.Empty);

    public RegisterTests(PasswordlessApiFactory apiFactory)
    {
        _client = apiFactory.CreateClient()
            .AddPublicKey()
            .AddSecretKey()
            .AddUserAgent();
    }

    [Fact]
    public async Task I_can_retrieve_token_to_start_registration()
    {
        // Arrange
        var request = TokenGenerator.Generate();

        // Act
        var response = await _client.PostAsJsonAsync("/register/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var registerTokenResponse = await response.Content.ReadFromJsonAsync<RegisterEndpoints.RegisterTokenResponse>();
        registerTokenResponse.Should().NotBeNull();
        registerTokenResponse!.Token.Should().StartWith("register_");
    }

    [Fact]
    public async Task I_can_retrieve_the_credential_create_options_and_session_token_for_creating_a_new_user()
    {
        // Arrange
        var tokenRequest = TokenGenerator.Generate();
        var tokenResponse = await _client.PostAsJsonAsync("/register/token", tokenRequest);
        var registerTokenResponse = await tokenResponse.Content.ReadFromJsonAsync<RegisterEndpoints.RegisterTokenResponse>();

        var registrationBeginRequest = new FidoRegistrationBeginDTO
        {
            Token = registerTokenResponse!.Token,
            Origin = PasswordlessApiFactory.OriginUrl,
            RPID = PasswordlessApiFactory.RpId
        };

        // Act
        using var registrationBeginResponse = await _client.PostAsJsonAsync("/register/begin", registrationBeginRequest);

        // Assert
        registrationBeginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionResponse = await registrationBeginResponse.Content.ReadFromJsonAsync<SessionResponse<CredentialCreateOptions>>();
        sessionResponse.Should().NotBeNull();
        sessionResponse!.Data.User.DisplayName.Should().Be(tokenRequest.DisplayName);
        sessionResponse.Data.User.Name.Should().Be(tokenRequest.Username);
    }

    [Fact]
    public async Task I_can_use_a_passkey_to_register_a_new_user()
    {
        // Arrange
        using var driver = WebDriverFactory.GetWebDriver(PasswordlessApiFactory.OriginUrl);

        // Act
        using var registerCompleteResponse = await UserHelpers.RegisterNewUser(_client, driver);

        // Assert
        registerCompleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var registerCompleteToken = await registerCompleteResponse.Content.ReadFromJsonAsync<TokenResponse>();
        registerCompleteToken!.Token.Should().StartWith("verify_");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}