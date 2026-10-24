// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Configures OAuth authentication for realtime HTTP and WebSocket clients.
/// </summary>
public static class RealtimeAuthenticationExtensions
{
    /// <summary>
    /// Registers database-backed authentication and query token support for realtime endpoints.
    /// </summary>
    /// <param name="services">
    /// The services to configure.
    /// </param>
    /// <returns>
    /// The service collection.
    /// </returns>
    public static IServiceCollection AddRealtimeAuthentication(this IServiceCollection services)
    {
        services.AddOAuthAuthentication();
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Events.OnMessageReceived = context =>
            {
                if (!context.Request.Path.StartsWithSegments("/signalr", StringComparison.OrdinalIgnoreCase) &&
                    !context.Request.Path.StartsWithSegments("/notification-server", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                string? token = context.Request.Query["access_token"];
                if (string.IsNullOrWhiteSpace(token))
                {
                    return Task.CompletedTask;
                }

                const string bearerPrefix = "Bearer ";
                context.Token = token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                    ? token[bearerPrefix.Length..].Trim()
                    : token;

                return Task.CompletedTask;
            };
        });
        return services;
    }
}