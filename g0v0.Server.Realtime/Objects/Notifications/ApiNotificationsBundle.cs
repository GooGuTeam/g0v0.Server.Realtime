// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// The response of <c>GET /api/v2/notifications</c>.
/// </summary>
public sealed class ApiNotificationsBundle
{
    /// <summary>
    /// Gets or sets a value indicating whether more notifications are available.
    /// </summary>
    [JsonProperty("has_more")]
    public bool HasMore { get; set; }

    /// <summary>
    /// Gets or sets the notifications, sorted by descending ID.
    /// </summary>
    [JsonProperty("notifications")]
    public IReadOnlyList<ApiNotification> Notifications { get; set; } = [];

    /// <summary>
    /// Gets or sets the total number of unread notifications.
    /// </summary>
    [JsonProperty("unread_count")]
    public int UnreadCount { get; set; }

    /// <summary>
    /// Gets or sets the WebSocket endpoint receiving the <c>new</c> events.
    /// </summary>
    [JsonProperty("notification_endpoint")]
    public string NotificationEndpoint { get; set; } = string.Empty;
}