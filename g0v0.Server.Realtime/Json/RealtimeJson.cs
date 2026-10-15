// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace g0v0.Server.Realtime.Json;

/// <summary>
/// Shared Newtonsoft.Json configuration of the realtime server.
/// </summary>
internal static class RealtimeJson
{
    /// <summary>
    /// Settings of the notification WebSocket frames: snake_case property names
    /// and snake_case enum names, mirroring the lazer notification server
    /// protocol.
    /// </summary>
    internal static readonly JsonSerializerSettings SocketFrame = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        Converters =
        [
            new StringEnumConverter(new SnakeCaseNamingStrategy(), allowIntegerValues: false),
        ],
    };

    /// <summary>
    /// Settings of the persisted notification <c>details</c> payload: the
    /// details types carry explicit snake_case property names, so the camelCase
    /// resolver only affects the properties without one.
    /// </summary>
    internal static readonly JsonSerializerSettings NotificationDetails = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    /// <summary>
    /// Contract resolver of the MVC JSON input/output: the DTOs carry explicit
    /// snake_case property names, so the camelCase resolver only affects the
    /// properties without one (including anonymous response payloads).
    /// </summary>
    internal static readonly IContractResolver MvcContractResolver = new CamelCasePropertyNamesContractResolver();
}