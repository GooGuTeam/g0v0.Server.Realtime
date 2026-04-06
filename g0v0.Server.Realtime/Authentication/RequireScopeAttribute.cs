// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Authorization;

namespace g0v0.Server.Realtime.Authentication;

/// <summary>
/// Convenience attribute to require specific OAuth scopes on a controller or action.
/// Translates to a dynamic <c>Scope:xxx,yyy</c> policy resolved by <see cref="ScopePolicyProvider"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireScopeAttribute : AuthorizeAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequireScopeAttribute"/> class.
    /// </summary>
    /// <param name="scopes">The required scopes.</param>
    public RequireScopeAttribute(params string[] scopes)
    {
        Policy = $"Scope:{string.Join(",", scopes)}";
    }
}
