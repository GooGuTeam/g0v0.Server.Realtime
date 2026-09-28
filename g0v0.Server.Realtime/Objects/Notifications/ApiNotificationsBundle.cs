// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// The response of <c>GET /api/v2/notifications</c>, mirroring the bundle of
/// osu-web's <c>NotificationsController::index</c> (see its documented
/// response format and lazer's <c>APINotificationsBundle</c>).
/// </summary>
public sealed class ApiNotificationsBundle
{
    /// <summary>
    /// Gets or sets a value indicating whether more notifications are available.
    /// </summary>
    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    /// <summary>
    /// Gets or sets the notifications, sorted by descending ID.
    /// </summary>
    [JsonPropertyName("notifications")]
    public IReadOnlyList<ApiNotification> Notifications { get; set; } = [];

    /// <summary>
    /// Gets or sets the total number of unread notifications.
    /// </summary>
    [JsonPropertyName("unread_count")]
    public int UnreadCount { get; set; }

    /// <summary>
    /// Gets or sets the WebSocket endpoint receiving the <c>new</c> events.
    /// </summary>
    [JsonPropertyName("notification_endpoint")]
    public string NotificationEndpoint { get; set; } = string.Empty;
}