// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Base payload of the channel message notifications: the notifiable object is
/// the channel the message was posted in and the receivers are derived from
/// the channel's users.
/// </summary>
public abstract class ChannelNotificationBase : NotificationDetail
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelNotificationBase"/> class.
    /// </summary>
    /// <param name="sourceUserId">The sender user ID.</param>
    /// <param name="receiverIds">The receiving user IDs.</param>
    protected ChannelNotificationBase(int sourceUserId, IReadOnlyList<int> receiverIds)
    {
        SourceUserId = sourceUserId;
        ReceiverIds = receiverIds;
    }

    /// <summary>
    /// Gets or sets the channel ID the message was sent in.
    /// </summary>
    [JsonProperty("channel_id")]
    public int ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the lower-cased channel type (for example <c>pm</c>).
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username of the message sender, carried into the
    /// details payload.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the channel, carried into the details payload
    /// of the notification kinds that include it.
    /// </summary>
    [JsonIgnore]
    public string ChannelName { get; set; } = string.Empty;

    /// <inheritdoc />
    [JsonProperty("object_type")]
    public override string ObjectType => "channel";

    /// <inheritdoc />
    [JsonProperty("object_id")]
    public override int ObjectId => ChannelId;

    /// <inheritdoc />
    [JsonProperty("source_user_id")]
    public override int SourceUserId { get; }

    /// <inheritdoc />
    [JsonProperty("receiver_ids")]
    public override IReadOnlyList<int> ReceiverIds { get; }

    /// <summary>
    /// Creates the base channel message details shared by every kind.
    /// </summary>
    /// <typeparam name="TDetails">The concrete details type.</typeparam>
    /// <returns>The populated details.</returns>
    protected TDetails CreateDetails<TDetails>()
        where TDetails : ChannelMessageDetails, new()
    {
        return new TDetails
        {
            Title = Title,
            Type = Type.ToLowerInvariant(),
            CoverUrl = CoverUrl,
            Username = Username,
        };
    }
}