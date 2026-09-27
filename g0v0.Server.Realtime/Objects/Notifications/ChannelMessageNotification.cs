// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// A private message notification, mirroring the lazer API <c>ChannelMessage</c>.
/// </summary>
public sealed class ChannelMessageNotification : NotificationDetail
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelMessageNotification"/> class.
    /// </summary>
    /// <param name="sourceUserId">The sender user ID.</param>
    /// <param name="receiverIds">The receiving user IDs.</param>
    public ChannelMessageNotification(int sourceUserId, IReadOnlyList<int> receiverIds)
    {
        SourceUserId = sourceUserId;
        ReceiverIds = receiverIds;
    }

    /// <summary>
    /// Gets or sets the channel ID the message was sent in.
    /// </summary>
    [JsonProperty("channel_id")]
    [JsonPropertyName("channel_id")]
    public int ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the notification type (e.g. <c>pm</c> or <c>team</c>).
    /// </summary>
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <inheritdoc />
    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public override NotificationName Name => NotificationName.ChannelMessage;

    /// <inheritdoc />
    [JsonProperty("object_type")]
    [JsonPropertyName("object_type")]
    public override string ObjectType => "channel";

    /// <inheritdoc />
    [JsonProperty("object_id")]
    [JsonPropertyName("object_id")]
    public override int ObjectId => ChannelId;

    /// <inheritdoc />
    [JsonProperty("source_user_id")]
    [JsonPropertyName("source_user_id")]
    public override int SourceUserId { get; }

    /// <inheritdoc />
    [JsonProperty("receiver_ids")]
    [JsonPropertyName("receiver_ids")]
    public override IReadOnlyList<int> ReceiverIds { get; }

    /// <inheritdoc />
    public override object Details => new ChannelMessageDetails
    {
        Title = Title,
        Type = Type.ToLowerInvariant(),
        CoverUrl = CoverUrl,
    };
}