// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Authentication;
using Microsoft.AspNetCore.SignalR;

namespace g0v0.Server.Realtime;

/// <summary>
/// Resolves the SignalR user identifier from JWT claims.
/// Uses only the database-verified user ID supplied by authentication.
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
        string? userId = connection.User.FindFirst(OAuthClaimTypes.UserId)?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        _logger.LogWarning("Could not resolve a database-verified user ID for connection {ConnectionId}.", connection.ConnectionId);
        return null;
    }
}