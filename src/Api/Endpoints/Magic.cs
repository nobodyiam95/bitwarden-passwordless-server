using System.Net;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Passwordless.Api.Authorization;
using Passwordless.Api.OpenApi;
using Passwordless.Common.MagicLinks.Models;
using Passwordless.Service.Features;
using Passwordless.Service.Helpers;
using Passwordless.Service.MagicLinks;
using Passwordless.Service.MagicLinks.Extensions;
using Passwordless.Service.Models;
using static Microsoft.AspNetCore.Http.Results;

namespace Passwordless.Api.Endpoints;

public static class MagicEndpoints
{
    /// <summary>
    /// Name of the Magic Links Rate Limiter Policy
    /// </summary>
    public const string RateLimiterPolicy = nameof(MagicEndpoints);

    /// <summary>
    /// Adds a rate limiter policy for the MagicEndpoints. Each tenant will have its own partition.
    /// </summary>
    /// <param name="builder">The RateLimiterOptions builder.</param>
    public static void AddMagicRateLimiterPolicy(this RateLimiterOptions builder) =>
        builder.AddPolicy(RateLimiterPolicy, context =>
        {
            var tenant = context.User.FindFirstValue(CustomClaimTypes.AccountName) ?? "<global>";

            return RateLimitPartition.GetFixedWindowLimiter(tenant, _ =>
                new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(5), PermitLimit = 10, QueueLimit = 0, AutoReplenishment = true }
            );
        });

    /// <summary>
    /// Maps the magic link endpoints.
    /// </summary>
    /// <param name="app">The <see cref="IEndpointRouteBuilder"/> object.</param>
    public static void MapMagicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/magic-links")
            .RequireCors("default")
            .RequireSecretKey()
            .WithTags(OpenApiTags.MagicLinks);

        group.MapPost("/send", SendMagicLinkAsync)
            .WithParameterValidation()
            .RequireRateLimiting(RateLimiterPolicy);
    }

    /// <summary>
    /// Sends an e-mail containing a magic link template allowing users to login.
    ///
    /// Warning: Verify the e-mail address matches the user identifier in your backend.
    /// </summary>
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), (int)HttpStatusCode.BadRequest, MediaTypeNames.Application.ProblemJson)]
    public static async Task<IResult> SendMagicLinkAsync(
        [FromBody] SendMagicLinkRequest request,
        [FromServices] IFeatureContextProvider provider,
        [FromServices] MagicLinkService magicLinkService)
    {
        if (!(await provider.UseContext()).IsMagicLinksEnabled)
            throw new ApiException("You must enable Magic Links in settings in order to use this feature.", 403);

        await magicLinkService.SendMagicLinkAsync(request.ToDto());

        return NoContent();
    }
}