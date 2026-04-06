// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Authorization;

namespace g0v0.Server.Realtime.Authentication;

/// <summary>
/// Authorization requirement that demands the authenticated user's token
/// contains all the specified OAuth scopes (unless the token is from a known client).
/// </summary>
public class ScopeAuthorizationRequirement(params string[] scopes) : IAuthorizationRequirement
{
    /// <summary>
    /// Gets the required scopes.
    /// </summary>
    public string[] Scopes { get; } = scopes;
}