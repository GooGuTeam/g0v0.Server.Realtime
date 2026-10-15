// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Details of a <c>channel_announcement</c> notification.
/// </summary>
public sealed class ChannelAnnouncementDetails : ChannelMessageDetails
{
    /// <summary>
    /// Gets or sets the ID of the announcement channel.
    /// </summary>
    [JsonProperty("channel_id")]
    public int ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the name of the announcement channel.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}