// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Security.Claims;
using g0v0.Server.Common.Authentication;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Hubs;

[TestFixture]
public class JwtUserIdProviderTests
{
    [TestCase("456")]
    [TestCase(null)]
    public void GetUserId_UsesOnlyDatabaseVerifiedClaim(string? verifiedUserId)
    {
        List<Claim> claims = [new("sub", "123"), new(ClaimTypes.NameIdentifier, "123")];
        if (verifiedUserId != null)
        {
            claims.Add(new Claim(OAuthClaimTypes.UserId, verifiedUserId));
        }

        DefaultConnectionContext connection = new();
        connection.Features.Set<IConnectionUserFeature>(new TestUserFeature
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims)),
        });
        HubConnectionContext context = new(connection, new HubConnectionContextOptions(), NullLoggerFactory.Instance);
        JwtUserIdProvider provider = new(NullLoggerFactory.Instance);

        Assert.That(provider.GetUserId(context), Is.EqualTo(verifiedUserId));
    }

    private sealed class TestUserFeature : IConnectionUserFeature
    {
        public ClaimsPrincipal? User { get; set; }
    }
}