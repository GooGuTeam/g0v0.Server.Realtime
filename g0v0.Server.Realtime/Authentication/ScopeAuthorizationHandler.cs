// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace g0v0.Server.Realtime.Authentication;

/// <summary>
/// Handles <see cref="ScopeAuthorizationRequirement"/> by checking the user's
/// OAuth scope claims. Tokens from known clients (osu! client / web client)
/// bypass scope checks, mirroring the Python <c>_validate_token</c> logic.
/// </summary>
public class ScopeAuthorizationHandler(ConfigurationManager<GeneralConfiguration> config)
    : AuthorizationHandler<ScopeAuthorizationRequirement>
{
    /// <summary>
    /// Evaluates whether the current user satisfies the required OAuth scopes.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <param name="requirement">The scope requirement.</param>
    /// <returns>A completed task.</returns>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeAuthorizationRequirement requirement)
    {
        var generalConfig = config.Value;

        // Client tokens (from known osu! client IDs) bypass scope checks
        var clientIdClaim = context.User.FindFirst(OAuthClaimTypes.ClientId);
        if (clientIdClaim != null)
        {
            var clientId = clientIdClaim.Value;
            if (clientId == generalConfig.OsuClientId.ToString() ||
                clientId == generalConfig.OsuWebClientId.ToString())
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        // Check scopes from claims
        var scopeClaims = context.User.FindAll(OAuthClaimTypes.Scope)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        // Wildcard scope "*" grants all permissions
        if (scopeClaims.Contains("*") ||
            requirement.Scopes.All(s => scopeClaims.Contains(s)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}