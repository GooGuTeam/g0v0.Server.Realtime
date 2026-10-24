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
    /// <param name="context">
    /// The hub caller context.
    /// </param>
    /// <returns>
    /// The osu! user id.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no user identifier can be resolved.
    /// </exception>
    public static int GetUserId(this HubCallerContext context)
    {
        return context.UserIdentifier != null && int.TryParse(context.UserIdentifier, out int userId)
            ? userId
            : throw new InvalidOperationException("Unable to determine user ID from UserIdentifier.");
    }
}