// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Details of a <c>channel_mention</c> notification, mirroring osu-web's
/// <c>Notifications\ChannelMention::getDetails()</c>.
/// </summary>
public sealed class ChannelMentionDetails : ChannelMessageDetails
{
    /// <summary>
    /// Gets or sets the ID of the message containing the mention.
    /// </summary>
    [JsonProperty("message_id")]
    [JsonPropertyName("message_id")]
    public int MessageId { get; set; }

    /// <summary>
    /// Gets or sets the name of the channel the mention happened in.
    /// </summary>
    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}