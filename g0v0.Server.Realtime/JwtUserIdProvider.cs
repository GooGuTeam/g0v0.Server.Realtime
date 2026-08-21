// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace g0v0.Server.Realtime;

/// <summary>
/// Resolves the SignalR user identifier from JWT claims.
/// Falls back to reading the Authorization header directly when the
/// ASP.NET Core authentication pipeline does not populate claims.
/// </summary>
public class JwtUserIdProvider(ILoggerFactory loggerFactory) : IUserIdProvider
{
    private readonly ILogger<JwtUserIdProvider> _logger = loggerFactory.CreateLogger<JwtUserIdProvider>();

    /// <summary>
    /// Resolves the user identifier from the current connection's claims.
    /// </summary>
    /// <param name="connection">The hub connection.</param>
    /// <returns>The resolved user identifier, or <see langword="null"/> if none was found.</returns>
    public string? GetUserId(HubConnectionContext connection)
    {
        Claim? claim = connection.User.FindFirst(static claim =>
            (claim.Type is ClaimTypes.NameIdentifier or "sub")
            && !string.IsNullOrWhiteSpace(claim.Value));

        if (claim?.Value != null)
        {
            return claim.Value;
        }

        // Fallback: parse the Authorization header manually (same logic as HubCallerContextExtensions.GetUserId).
        HttpContext? httpContext = connection.GetHttpContext();

        if (httpContext != null &&
            httpContext.Request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues authHeader) &&
            !string.IsNullOrEmpty(authHeader))
        {
            const string bearerPrefix = "Bearer ";
            string headerValue = authHeader.ToString();

            if (headerValue.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string token = headerValue[bearerPrefix.Length..].Trim();

                try
                {
                    JwtSecurityToken jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(token);
                    Claim? subClaim = jwtToken.Claims.FirstOrDefault(c => string.Equals(c.Type, "sub", StringComparison.Ordinal));
                    string? subValue = subClaim?.Value;

                    if (!string.IsNullOrEmpty(subValue))
                    {
                        return subValue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "JwtUserIdProvider: failed to parse JWT from Authorization header. Connection {ConnectionId}.",
                        connection.ConnectionId);
                }
            }
        }

        string claimTypes = string.Join(", ", connection.User.Claims.Select(c => $"{c.Type}={c.Value}"));
        _logger.LogWarning(
            "JwtUserIdProvider: could not resolve user ID. Connection {ConnectionId} has claims: [{Claims}]",
            connection.ConnectionId,
            claimTypes);

        return null;
    }
}