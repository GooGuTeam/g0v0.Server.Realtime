// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.SignalR;

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Provides helpers for reading authenticated values from a hub caller context.
/// </summary>
public static class HubCallerContextExtensions
{
    /// <summary>
    /// Returns the osu! user id for the supplied <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The hub caller context.</param>
    /// <returns>The osu! user id.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no user identifier can be resolved.</exception>
    public static int GetUserId(this HubCallerContext context)
    {
        try
        {
            HttpContext? httpContext = context.GetHttpContext();

            if (httpContext != null)
            {
                if (httpContext.Request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues authHeader) &&
                    !string.IsNullOrEmpty(authHeader))
                {
                    const string bearerPrefix = "Bearer ";
                    string headerValue = authHeader.ToString();

                    if (headerValue.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string token = headerValue[bearerPrefix.Length..].Trim();

                        System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler handler = new();
                        System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwtToken = handler.ReadJwtToken(token);
                        System.Security.Claims.Claim? subClaim = jwtToken.Claims.FirstOrDefault(c => string.Equals(c.Type, "sub", StringComparison.Ordinal));
                        string? subValue = subClaim?.Value;

                        if (!string.IsNullOrEmpty(subValue) && int.TryParse(subValue, out int userId))
                        {
                            return userId;
                        }
                    }
                }
            }
        }
        catch
        {
            // GetHttpContext may throw if the context does not support Features (e.g. in tests).
        }

        // Fallback to UserIdentifier (used by tests and legacy paths).
        return context.UserIdentifier != null && int.TryParse(context.UserIdentifier, out int fallbackUserId)
            ? fallbackUserId
            : throw new InvalidOperationException("Unable to determine user ID from JWT or UserIdentifier.");
    }
}