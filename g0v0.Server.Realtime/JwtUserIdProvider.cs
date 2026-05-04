// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace g0v0.Server.Realtime;

/// <summary>
/// Resolves the SignalR user identifier from JWT claims.
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

        var userId = claim?.Value;

        if (userId != null)
        {
            return userId;
        }

        var claimTypes = string.Join(", ", connection.User.Claims.Select(c => $"{c.Type}={c.Value}"));
        _logger.LogWarning(
            "JwtUserIdProvider: could not resolve user ID. Connection {ConnectionId} has claims: [{Claims}]",
            connection.ConnectionId,
            claimTypes);

        return null;
    }
}