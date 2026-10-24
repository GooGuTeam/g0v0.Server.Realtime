// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// A public channel mention notification.
/// </summary>
public sealed class ChannelMentionNotification : ChannelNotificationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelMentionNotification"/> class.
    /// </summary>
    /// <param name="sourceUserId">The sender user ID.</param>
    /// <param name="receiverIds">The receiving user IDs.</param>
    public ChannelMentionNotification(int sourceUserId, IReadOnlyList<int> receiverIds)
        : base(sourceUserId, receiverIds)
    {
    }

    /// <summary>
    /// Gets or sets the ID of the message containing the mention.
    /// </summary>
    [JsonIgnore]
    public int MessageId { get; set; }

    /// <inheritdoc />
    [JsonProperty("name")]
    public override NotificationName Name => NotificationName.ChannelMention;

    /// <inheritdoc />
    public override object Details
    {
        get
        {
            ChannelMentionDetails details = CreateDetails<ChannelMentionDetails>();
            details.MessageId = MessageId;
            details.Name = ChannelName;
            return details;
        }
    }
}