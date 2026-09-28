// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Extensions;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Objects.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// Notification listing and read-marking endpoints.
/// </summary>
[ApiController]
[Route("notifications")]
[Route("api/v2/notifications")]
public class NotificationsController(INotificationRepository notificationRepository, ConfigManager configManager) : ControllerBase
{
    /// <summary>
    /// The number of notifications per page.
    /// </summary>
    private const int Limit = 50;

    /// <summary>
    /// Lists the notifications of the current user, sorted by descending ID.
    /// </summary>
    /// <param name="maxId">When set, only notifications with a smaller ID are returned.</param>
    /// <param name="unread">Whether only unread notifications are returned; defaults to <c>true</c> like the reference endpoints.</param>
    /// <returns>The notification bundle.</returns>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Index([FromQuery] long? maxId, [FromQuery] bool? unread)
    {
        int currentUserId = HttpContext.GetUserId();
        bool unreadOnly = unread ?? true;

        IReadOnlyList<UserNotification> records = await notificationRepository.GetUserNotificationsAsync(
            currentUserId,
            unreadOnly,
            maxId,
            Limit + 1);
        bool hasMore = records.Count > Limit;
        if (hasMore)
        {
            records = [.. records.Take(Limit)];
        }

        ApiNotificationsBundle bundle = new()
        {
            HasMore = hasMore,
            Notifications = [.. records.Select(CreateNotification)],
            UnreadCount = await notificationRepository.CountUnreadAsync(currentUserId),
            NotificationEndpoint = BuildNotificationEndpoint(),
        };

        Response.Headers.CacheControl = "no-store";
        return Ok(bundle);
    }

    /// <summary>
    /// Marks notifications of the current user as read.
    /// </summary>
    /// <param name="request">The notifications and identities to mark read.</param>
    /// <returns>An empty response.</returns>
    [HttpPost("mark-read")]
    [Authorize]
    public async Task<IActionResult> MarkRead([FromBody] MarkNotificationsReadRequest? request)
    {
        int currentUserId = HttpContext.GetUserId();
        IReadOnlyList<NotificationIdentityParameter> entries = request?.Notifications ?? [];
        List<int> notificationIds = entries
            .Where(entry => entry.Id.HasValue)
            .Select(entry => entry.Id!.Value)
            .Distinct()
            .ToList();

        if (request?.Notifications == null)
        {
            foreach (NotificationIdentityParameter identity in request?.Identities ?? [])
            {
                IReadOnlyList<string> names = identity.Category == null
                    ? []
                    : NotificationCategories.NamesInCategory(identity.Category);
                IReadOnlyList<int> resolvedIds = await notificationRepository.GetNotificationIdsAsync(
                    names,
                    identity.ObjectType,
                    identity.ObjectId,
                    identity.Id);
                notificationIds = notificationIds.Union(resolvedIds).ToList();
            }
        }

        if (notificationIds.Count > 0)
        {
            await notificationRepository.MarkAsReadAsync(currentUserId, notificationIds);
        }

        return NoContent();
    }

    /// <summary>
    /// Transforms a delivery record into its API representation: the
    /// notification fields together with the per-user <c>is_read</c> flag.
    /// </summary>
    /// <param name="record">The delivery record with its notification loaded.</param>
    /// <returns>The API notification.</returns>
    private static ApiNotification CreateNotification(UserNotification record)
    {
        Notification notification = record.Notification!;
        return new ApiNotification
        {
            Id = notification.Id,
            Name = notification.Name.ToLowerInvariant(),
            CreatedAt = notification.CreatedAt,
            ObjectType = notification.ObjectType,
            ObjectId = notification.ObjectId,
            SourceUserId = notification.SourceUserId,
            IsRead = record.IsRead,
            Details = DeserializeDetails(notification.Details),
        };
    }

    private static JsonElement? DeserializeDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(details);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the WebSocket endpoint URL from the configured server URL,
    /// mirroring the reference implementation's <c>notification_endpoint</c>:
    /// the server URL with its trailing slash followed by
    /// <c>notification-server</c>, with the HTTP scheme upgraded to WS.
    /// </summary>
    /// <returns>The endpoint URL.</returns>
    private string BuildNotificationEndpoint()
    {
        GeneralConfig generalConfig = configManager.Get<GeneralConfig>();
        return (generalConfig.ServerUrl.AddSuffix("/") + "notification-server")
            .Replace("http://", "ws://", StringComparison.Ordinal)
            .Replace("https://", "wss://", StringComparison.Ordinal);
    }
}