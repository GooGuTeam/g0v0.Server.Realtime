// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Security.Claims;
using g0v0.Server.Common.Authentication;

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Provides helpers for reading authenticated values from an <see cref="HttpContext"/>.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Returns the osu! user ID for the supplied <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// The HTTP context.
    /// </param>
    /// <returns>
    /// The user ID.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no user identifier can be resolved.
    /// </exception>
    public static int GetUserId(this HttpContext context)
    {
        Claim? claim = context.User.FindFirst(OAuthClaimTypes.UserId);

        return claim != null && int.TryParse(claim.Value, out int userId)
            ? userId
            : throw new InvalidOperationException("Unable to determine user ID from the current claims.");
    }
}