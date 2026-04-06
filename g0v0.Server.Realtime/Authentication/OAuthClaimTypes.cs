// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Authentication;

/// <summary>
/// Defines custom claim types used for OAuth token authorization.
/// </summary>
public static class OAuthClaimTypes
{
    /// <summary>
    /// The claim type for the OAuth client ID.
    /// </summary>
    public const string ClientId = "client_id";

    /// <summary>
    /// The claim type for an individual OAuth scope.
    /// </summary>
    public const string Scope = "scope";
}
