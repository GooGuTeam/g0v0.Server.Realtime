// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Security.Claims;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace g0v0.Server.Realtime.Authentication;

/// <summary>
/// Custom JWT token handler that verifies tokens exist in the database
/// (oauth_tokens table) and are not expired. Also populates OAuth claims
/// (client_id, scope) from the database token record.
/// </summary>
public class DatabaseJwtTokenHandler(
    IServiceProvider serviceProvider,
    ILogger<DatabaseJwtTokenHandler> logger)
    : TokenHandler
{
    private readonly JsonWebTokenHandler _inner = new();

    /// <summary>
    /// Validates a JWT and verifies that the token record exists and is active in the database.
    /// </summary>
    /// <param name="token">The serialized JWT.</param>
    /// <param name="validationParameters">The token validation parameters.</param>
    /// <returns>The token validation result.</returns>
    public override Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        TokenValidationParameters validationParameters)
    {
        return ValidateTokenInternalAsync(token, validationParameters);
    }

    private async Task<TokenValidationResult> ValidateTokenInternalAsync(
        string token,
        TokenValidationParameters validationParameters)
    {
        // First, perform standard JWT validation (signature, expiry, audience, issuer)
        var result = await _inner.ValidateTokenAsync(token, validationParameters);

        if (!result.IsValid)
        {
            return result;
        }

        // Then, verify the token exists in the database and is not expired
        OAuthToken? tokenRecord;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var tokenRepository = scope.ServiceProvider.GetRequiredService<IOAuthTokenRepository>();
            tokenRecord = await tokenRepository.GetByAccessTokenAsync(token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate token against database");
            return new TokenValidationResult
            {
                IsValid = false,
                Exception = new SecurityTokenValidationException("Database token validation failed.", ex),
            };
        }

        if (tokenRecord == null)
        {
            logger.LogWarning("JWT token not found or expired in database");
            return new TokenValidationResult
            {
                IsValid = false,
                Exception = new SecurityTokenValidationException("Token not found or expired in database."),
            };
        }

        // Add OAuth claims from database token record
        var identity = result.ClaimsIdentity;
        identity.AddClaim(new Claim(OAuthClaimTypes.ClientId, tokenRecord.ClientId.ToString()));

        foreach (var s in tokenRecord.Scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            identity.AddClaim(new Claim(OAuthClaimTypes.Scope, s));
        }

        return result;
    }
}