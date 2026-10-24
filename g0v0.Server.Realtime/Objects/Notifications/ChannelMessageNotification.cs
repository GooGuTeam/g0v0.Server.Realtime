// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// A private message notification.
/// </summary>
public sealed class ChannelMessageNotification : ChannelNotificationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelMessageNotification"/> class.
    /// </summary>
    /// <param name="sourceUserId">
    /// The sender user ID.
    /// </param>
    /// <param name="receiverIds">
    /// The receiving user IDs.
    /// </param>
    public ChannelMessageNotification(int sourceUserId, IReadOnlyList<int> receiverIds)
        : base(sourceUserId, receiverIds)
    {
    }

    /// <inheritdoc />
    [JsonProperty("name")]
    public override NotificationName Name => NotificationName.ChannelMessage;

    /// <inheritdoc />
    public override object Details => CreateDetails<ChannelMessageDetails>();
}