// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// A team channel message notification.
/// </summary>
public sealed class ChannelTeamNotification : ChannelNotificationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelTeamNotification"/> class.
    /// </summary>
    /// <param name="sourceUserId">The sender user ID.</param>
    /// <param name="receiverIds">The receiving user IDs.</param>
    public ChannelTeamNotification(int sourceUserId, IReadOnlyList<int> receiverIds)
        : base(sourceUserId, receiverIds)
    {
    }

    /// <inheritdoc />
    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public override NotificationName Name => NotificationName.ChannelTeam;

    /// <inheritdoc />
    public override object Details
    {
        get
        {
            ChannelTeamDetails details = CreateDetails<ChannelTeamDetails>();
            details.Name = ChannelName;
            return details;
        }
    }
}