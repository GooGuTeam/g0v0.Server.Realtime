// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// Exposes the runtime health counters of the realtime server.
/// </summary>
/// <remarks>
/// The endpoint is anonymous so orchestrators and the monitoring stack can poll
/// it without a user token. The payload carries <c>identify</c> so a shared
/// monitor can tell the stable and realtime servers apart.
/// </remarks>
[ApiController]
[Route("health")]
public class HealthController(
    PlayerManager playerManager,
    RoomManager roomManager,
    INotificationServer notificationServer) : ControllerBase
{
    /// <summary>
    /// Reports the live counters of the realtime server.
    /// </summary>
    /// <returns>The health payload.</returns>
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            identify = "realtime",
            players_online = playerManager.GetAllPlayers().Count(),
            multiplayer_rooms = roomManager.GetAllRooms().Count(),
            websocket_connections = notificationServer.ConnectionCount,
        });
    }
}