using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Passwordless.Api.IntegrationTests.Helpers;
using Passwordless.Service.Models;
using Passwordless.Service.Storage.Ef;
using Xunit;
using static Passwordless.Api.IntegrationTests.Helpers.App.CreateAppHelpers;

namespace Passwordless.Api.IntegrationTests.Endpoints.App;

public class AppTests : IClassFixture<PasswordlessApiFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly PasswordlessApiFactory _factory;


    public AppTests(PasswordlessApiFactory apiFactory)
    {
        _factory = apiFactory;
        _client = apiFactory.CreateClient()
            .AddManagementKey();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1")]
    public async Task I_cannot_create_an_account_with_an_invalid_name(string name)
    {
        // Act
        using var response = await _client.CreateApplication(name);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        content.Should().NotBeNull();
        content!.Title.Should().Be("accountName needs to be alphanumeric and start with a letter");
    }

    [Fact]
    public async Task I_can_create_an_account_with_a_valid_name()
    {
        // Arrange
        const string accountName = "anders";

        // Act
        using var response = await _client.CreateApplication(accountName);

        // Assert
        response.Should().NotBeNull();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<AccountKeysCreation>();
        content.Should().NotBeNull();
        content!.Message.Should().Be("Store keys safely. They will only be shown to you once.");
        content.ApiKey1.Should().NotBeNullOrWhiteSpace();
        content.ApiKey2.Should().NotBeNullOrWhiteSpace();
        content.ApiSecret1.Should().NotBeNullOrWhiteSpace();
        content.ApiSecret2.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task I_can_create_an_app_and_its_features_will_be_set_correctly()
    {
        // Arrange
        var name = $"app{Guid.NewGuid():N}";

        // Act
        using var res = await _client.CreateApplication(name);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();

        var appFeature = await scope.ServiceProvider.GetRequiredService<ITenantStorageFactory>()
            .Create(name)
            .GetAppFeaturesAsync();

        appFeature.Should().NotBeNull();
        appFeature!.Tenant.Should().Be(name);
        appFeature.EventLoggingIsEnabled.Should().BeFalse();
        appFeature.EventLoggingRetentionPeriod.Should().Be(365);
        appFeature.DeveloperLoggingEndsAt.Should().BeNull();
    }

    [Fact]
    public async Task I_can_set_event_logging_retention_period()
    {
        // Arrange
        const int expectedEventLoggingRetentionPeriod = 30;
        var name = $"app{Guid.NewGuid():N}";
        using var appCreateResponse = await _client.CreateApplication(name);
        var appCreateDto = await appCreateResponse.Content.ReadFromJsonAsync<AccountKeysCreation>();
        var setFeatureRequest = new SetFeaturesDto { EventLoggingRetentionPeriod = expectedEventLoggingRetentionPeriod };
        using var appHttpClient = _factory.CreateClient().AddSecretKey(appCreateDto!.ApiSecret1);

        // Act
        using var setFeatureResponse = await appHttpClient.PostAsJsonAsync("/apps/features", setFeatureRequest);

        // Assert
        setFeatureResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = _factory.Services.CreateScope();

        var appFeature = await scope.ServiceProvider.GetRequiredService<ITenantStorageFactory>().Create(name).GetAppFeaturesAsync();
        appFeature.Should().NotBeNull();
        appFeature!.EventLoggingRetentionPeriod.Should().Be(expectedEventLoggingRetentionPeriod);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task I_can_not_set_the_event_logging_retention_period_to_an_invalid_value(int invalidRetentionPeriod)
    {
        // Arrange
        var name = $"app{Guid.NewGuid():N}";
        using var appCreateResponse = await _client.CreateApplication(name);
        var appCreateDto = await appCreateResponse.Content.ReadFromJsonAsync<AccountKeysCreation>();
        var setFeatureRequest = new SetFeaturesDto { EventLoggingRetentionPeriod = invalidRetentionPeriod };
        using var appHttpClient = _factory.CreateClient().AddSecretKey(appCreateDto!.ApiSecret1);

        // Act
        using var setFeatureResponse = await appHttpClient.PostAsJsonAsync("/apps/features", setFeatureRequest);

        // Assert
        setFeatureResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problemDetails = await setFeatureResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("One or more validation errors occurred.");
    }

    [Fact]
    public async Task I_can_manage_an_apps_features()
    {
        // Arrange
        const int expectedEventLoggingRetentionPeriod = 30;

        var name = $"app{Guid.NewGuid():N}";
        _ = await _client.CreateApplication(name);
        var manageFeatureRequest = new ManageFeaturesDto { EventLoggingRetentionPeriod = expectedEventLoggingRetentionPeriod, EventLoggingIsEnabled = true };

        // Act
        var manageFeatureResponse = await _client.PostAsJsonAsync($"/admin/apps/{name}/features", manageFeatureRequest);

        // Assert
        manageFeatureResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = _factory.Services.CreateScope();

        var appFeature = await scope.ServiceProvider.GetRequiredService<ITenantStorageFactory>().Create(name).GetAppFeaturesAsync();
        appFeature.Should().NotBeNull();
        appFeature!.EventLoggingRetentionPeriod.Should().Be(expectedEventLoggingRetentionPeriod);
        appFeature.EventLoggingIsEnabled.Should().BeTrue();
        appFeature.DeveloperLoggingEndsAt.Should().BeNull();
    }

    [Fact]
    public async Task I_can_get_an_apps_features()
    {
        // Arrange
        const int expectedEventLoggingRetentionPeriod = 30;

        var name = $"app{Guid.NewGuid():N}";
        _ = await _client.CreateApplication(name);
        var manageAppFeatureRequest = new ManageFeaturesDto { EventLoggingRetentionPeriod = expectedEventLoggingRetentionPeriod, EventLoggingIsEnabled = true };
        _ = await _client.PostAsJsonAsync($"/admin/apps/{name}/features", manageAppFeatureRequest);

        // Act
        var getAppFeatureResponse = await _client.GetAsync($"/admin/apps/{name}/features");

        //Assert
        getAppFeatureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var appFeature = await getAppFeatureResponse.Content.ReadFromJsonAsync<AppFeatureDto>();
        appFeature.Should().NotBeNull();
        appFeature!.EventLoggingRetentionPeriod.Should().Be(expectedEventLoggingRetentionPeriod);
        appFeature.EventLoggingIsEnabled.Should().BeTrue();
        appFeature.DeveloperLoggingEndsAt.Should().BeNull();
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}