// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;

namespace g0v0.Server.Realtime.Tests.Notifications;

public sealed class FakeOAuthTokenRepository : IOAuthTokenRepository
{
    private readonly Dictionary<string, OAuthToken> _tokens = new(StringComparer.Ordinal);

    public void Register(int? userId, string accessToken, string scopes)
    {
        OAuthToken token = new()
        {
            UserId = userId,
            ClientId = 1,
            AccessToken = accessToken,
            RefreshToken = $"refresh-{Guid.NewGuid():N}",
            Scope = scopes,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };
        _tokens[token.AccessToken] = token;
    }

    public Task<OAuthToken?> GetByAccessTokenAsync(string token)
    {
        _tokens.TryGetValue(token, out OAuthToken? record);
        return Task.FromResult(record is { } found && found.ExpiresAt > DateTimeOffset.UtcNow ? found : null);
    }

    public Task<bool> CheckAccessTokenIsValidAsync(string token)
    {
        return Task.FromResult(_tokens.ContainsKey(token));
    }
}