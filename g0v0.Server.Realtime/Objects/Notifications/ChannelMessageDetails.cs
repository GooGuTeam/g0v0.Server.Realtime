// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Details of a <c>channel_message</c> notification.
/// </summary>
public class ChannelMessageDetails : NotificationDetails
{
    /// <summary>
    /// Gets or sets the (truncated) message content.
    /// </summary>
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lower-cased channel type (for example <c>pm</c>).
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar URL of the message sender.
    /// </summary>
    [JsonProperty("cover_url")]
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username of the message sender.
    /// </summary>
    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;
}