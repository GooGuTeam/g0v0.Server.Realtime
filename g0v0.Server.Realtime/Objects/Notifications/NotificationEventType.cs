// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Notifications;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// The types of events broadcast by the <see cref="NotificationServer"/>.
/// </summary>
/// <remarks>
/// Mirrors the events the lazer API chat server pushes to clients and the
/// osu-notification-server channel naming.
/// </remarks>
public enum NotificationEventType
{
    /// <summary>
    /// A new message was sent to a channel.
    /// </summary>
    ChatMessageNew,

    /// <summary>
    /// A channel was joined.
    /// </summary>
    ChatChannelJoin,

    /// <summary>
    /// A channel was left.
    /// </summary>
    ChatChannelPart,

    /// <summary>
    /// A user was silenced in a channel.
    /// </summary>
    ChatUserSilenced,

    /// <summary>
    /// A new private notification was created.
    /// </summary>
    NotificationNew,

    /// <summary>
    /// Notifications were marked as read.
    /// </summary>
    NotificationRead,
}