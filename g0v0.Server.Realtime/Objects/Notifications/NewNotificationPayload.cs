// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// The Newtonsoft.Json IPC payload of the <c>new_notification</c> notice.
/// </summary>
public sealed class NewNotificationPayload
{
    /// <summary>
    /// Gets or sets the notification detail.
    /// </summary>
    [JsonProperty("detail")]
    [JsonPropertyName("detail")]
    [Newtonsoft.Json.JsonConverter(typeof(NotificationDetailJsonConverter))]
    public NotificationDetail? Detail { get; set; }
}