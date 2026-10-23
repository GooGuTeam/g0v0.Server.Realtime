// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Security.Claims;
using g0v0.Server.Common.Authentication;
using g0v0.Server.Realtime.Extensions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Extensions;

[TestFixture]
public class HttpContextExtensionsTests
{
    [TestCase("456")]
    [TestCase(null)]
    [TestCase("invalid")]
    public void GetUserId_UsesOnlyVerifiedUserId(string? verifiedUserId)
    {
        List<Claim> claims = [new("sub", "123"), new(ClaimTypes.NameIdentifier, "123")];
        if (verifiedUserId != null)
        {
            claims.Add(new Claim(OAuthClaimTypes.UserId, verifiedUserId));
        }

        DefaultHttpContext context = new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) };
        if (string.Equals(verifiedUserId, "456", StringComparison.Ordinal))
        {
            Assert.That(context.GetUserId(), Is.EqualTo(456));
        }
        else
        {
            Assert.That(context.GetUserId, Throws.TypeOf<InvalidOperationException>());
        }
    }
}