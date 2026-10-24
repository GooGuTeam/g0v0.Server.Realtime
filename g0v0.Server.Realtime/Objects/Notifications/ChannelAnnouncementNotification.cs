// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// An announcement channel message notification.
/// </summary>
public sealed class ChannelAnnouncementNotification : ChannelNotificationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelAnnouncementNotification"/> class.
    /// </summary>
    /// <param name="sourceUserId">
    /// The sender user ID.
    /// </param>
    /// <param name="receiverIds">
    /// The receiving user IDs.
    /// </param>
    public ChannelAnnouncementNotification(int sourceUserId, IReadOnlyList<int> receiverIds)
        : base(sourceUserId, receiverIds)
    {
    }

    /// <inheritdoc />
    [JsonProperty("name")]
    public override NotificationName Name => NotificationName.ChannelAnnouncement;

    /// <inheritdoc />
    public override object Details
    {
        get
        {
            ChannelAnnouncementDetails details = CreateDetails<ChannelAnnouncementDetails>();
            details.ChannelId = ChannelId;
            details.Name = ChannelName;
            return details;
        }
    }
}