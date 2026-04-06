// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace g0v0.Server.Realtime.Authentication;

/// <summary>
/// Dynamic authorization policy provider that creates scope-based policies
/// on the fly for policy names matching the <c>Scope:xxx</c> pattern.
/// Falls back to the default provider for all other policy names.
/// </summary>
public class ScopePolicyProvider : IAuthorizationPolicyProvider
{
    private const string ScopePrefix = "Scope:";
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopePolicyProvider"/> class.
    /// </summary>
    /// <param name="options">Authorization options.</param>
    public ScopePolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    /// <summary>
    /// Gets the default authorization policy.
    /// </summary>
    /// <returns>The default authorization policy.</returns>
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <summary>
    /// Gets the fallback authorization policy.
    /// </summary>
    /// <returns>The fallback policy if configured; otherwise, <see langword="null"/>.</returns>
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    /// <summary>
    /// Gets an authorization policy by name, creating dynamic scope policies when needed.
    /// </summary>
    /// <param name="policyName">The policy name.</param>
    /// <returns>The resolved policy, or <see langword="null"/> if not found.</returns>
    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(ScopePrefix, StringComparison.Ordinal))
        {
            var scopes = policyName[ScopePrefix.Length..].Split(',');
            var builder = new AuthorizationPolicyBuilder();
            builder.RequireAuthenticatedUser();
            builder.AddRequirements(new ScopeAuthorizationRequirement(scopes));
            return builder.Build();
        }

        return await _fallback.GetPolicyAsync(policyName);
    }
}