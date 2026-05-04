// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using g0v0.Server.Realtime.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Extensions;

[TestFixture]
public class HubCallerContextExtensionsTests
{
    [Test]
    public void GetUserId_WhenBearerTokenContainsSubClaim_ReturnsClaimValue()
    {
        var handler = new JwtSecurityTokenHandler();
        string token = handler.WriteToken(new JwtSecurityToken(claims: [new Claim("sub", "123")]));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {token}";
        var context = new TestHubCallerContext(httpContext: httpContext);

        int userId = context.GetUserId();

        Assert.That(userId, Is.EqualTo(123));
    }

    [Test]
    public void GetUserId_WhenUserIdentifierExists_FallsBackToUserIdentifier()
    {
        var context = new TestHubCallerContext(userIdentifier: "456");

        int userId = context.GetUserId();

        Assert.That(userId, Is.EqualTo(456));
    }

    [Test]
    public void GetUserId_WhenNoJwtOrValidIdentifier_ThrowsInvalidOperationException()
    {
        var context = new TestHubCallerContext(userIdentifier: "not-an-int");

        Assert.That(() => context.GetUserId(), Throws.TypeOf<InvalidOperationException>());
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        private readonly FeatureCollection _features;
        private readonly IDictionary<object, object?> _items = new Dictionary<object, object?>();
        private readonly ClaimsPrincipal _user = new();

        public TestHubCallerContext(string connectionId = "connection-id", string? userIdentifier = null, HttpContext? httpContext = null)
        {
            ConnectionIdValue = connectionId;
            UserIdentifierValue = userIdentifier;
            _features = new FeatureCollection();

            if (httpContext != null)
            {
                RegisterHttpContextFeature(httpContext);
            }
        }

        public string ConnectionIdValue { get; }

        public string? UserIdentifierValue { get; }

        public override string ConnectionId => ConnectionIdValue;

        public override string? UserIdentifier => UserIdentifierValue;

        public override ClaimsPrincipal? User => _user;

        public override IDictionary<object, object?> Items => _items;

        public override IFeatureCollection Features => _features;

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }

        private void RegisterHttpContextFeature(HttpContext httpContext)
        {
            Assembly httpConnectionsAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(static assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Http.Connections")
                ?? Assembly.Load("Microsoft.AspNetCore.Http.Connections");

            Type signalRAssemblyType = httpConnectionsAssembly.GetType("Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature")
                ?? throw new InvalidOperationException("Unable to resolve the SignalR IHttpContextFeature type.");

            MethodInfo createMethod = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method => method.Name == nameof(DispatchProxy.Create) && method.IsGenericMethodDefinition)
                .MakeGenericMethod(signalRAssemblyType, typeof(HttpContextFeatureProxy));

            object feature = createMethod.Invoke(null, null)
                ?? throw new InvalidOperationException("Unable to create the SignalR IHttpContextFeature proxy instance.");

            ((HttpContextFeatureProxy)feature).HttpContext = httpContext;
            _features[signalRAssemblyType] = feature;
        }

        [SuppressMessage(
            "Performance",
            "CA1852:Seal internal types",
            Justification = "DispatchProxy requires an unsealed proxy base type.")]
        private class HttpContextFeatureProxy : DispatchProxy
        {
            public HttpContext? HttpContext { get; set; }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return targetMethod?.Name switch
                {
                    "get_HttpContext" => HttpContext,
                    "set_HttpContext" => SetHttpContext(args),
                    _ => throw new NotSupportedException($"Unsupported proxy method: {targetMethod?.Name}"),
                };
            }

            private object? SetHttpContext(object?[]? args)
            {
                HttpContext = (HttpContext?)args?[0];
                return null;
            }
        }
    }
}
