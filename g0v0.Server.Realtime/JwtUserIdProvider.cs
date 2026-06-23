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
        var claim = connection.User.FindFirst(static claim =>
            (claim.Type is ClaimTypes.NameIdentifier or "sub")
            && !string.IsNullOrWhiteSpace(claim.Value));

        if (claim?.Value != null)
        {
            return claim.Value;
        }

        // Fallback: parse the Authorization header manually (same logic as HubCallerContextExtensions.GetUserId).
        var httpContext = connection.GetHttpContext();

        if (httpContext != null &&
            httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            !string.IsNullOrEmpty(authHeader))
        {
            const string bearerPrefix = "Bearer ";
            var headerValue = authHeader.ToString();

            if (headerValue.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = headerValue.Substring(bearerPrefix.Length).Trim();

                try
                {
                    var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(token);
                    var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub");
                    var subValue = subClaim?.Value;

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

        var claimTypes = string.Join(", ", connection.User.Claims.Select(c => $"{c.Type}={c.Value}"));
        _logger.LogWarning(
            "JwtUserIdProvider: could not resolve user ID. Connection {ConnectionId} has claims: [{Claims}]",
            connection.ConnectionId,
            claimTypes);

        return null;
    }
}