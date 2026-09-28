// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Details of a <c>channel_message</c> notification, mirroring osu-web's
/// <c>Notifications\ChannelMessageBase::getDetails()</c>.
/// </summary>
public class ChannelMessageDetails : NotificationDetails
{
    /// <summary>
    /// Gets or sets the (truncated) message content.
    /// </summary>
    [JsonProperty("title")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lower-cased channel type (for example <c>pm</c>).
    /// </summary>
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar URL of the message sender.
    /// </summary>
    [JsonProperty("cover_url")]
    [JsonPropertyName("cover_url")]
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username of the message sender, mirroring the
    /// <c>username</c> osu-web's <c>BroadcastNotificationBase::makeNotification()</c>
    /// adds to every notification's details.
    /// </summary>
    [JsonProperty("username")]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
}