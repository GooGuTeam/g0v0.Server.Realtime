// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Objects.Notifications;
using g0v0.Server.Realtime.Objects.Players;

namespace g0v0.Server.Realtime.Notifications;

/// <summary>
/// Manages real-time chat connections and channel memberships, persists
/// messages/channels, and broadcasts chat and notification events to the
/// connected <see cref="IPlayer"/> instances.
/// </summary>
/// <remarks>
/// This is the C# counterpart of the lazer API <c>ChatServer</c> combined with
/// osu-web's chat model: connections are tracked in memory, channel membership
/// and read markers are persisted through <see cref="IChatRepository"/>, the
/// activity of public channels lives in a Redis sorted set, and every event is
/// broadcast to the matching <see cref="IPlayer"/> instances by calling their
/// <c>On...</c> methods.
/// </remarks>
public interface INotificationServer
{
    /// <summary>
    /// Registers a player's connection with the server.
    /// </summary>
    /// <param name="player">The player that connected.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Connect(IPlayer player);

    /// <summary>
    /// Removes a player's connection and cleans up channel memberships.
    /// </summary>
    /// <param name="player">The player that disconnected.</param>
    /// <param name="kicked">Whether the player was replaced by another connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Disconnect(IPlayer player, bool kicked = false);

    /// <summary>
    /// Joins a player to a channel and persists the membership if needed.
    /// </summary>
    /// <param name="player">The player joining.</param>
    /// <param name="channelId">The channel ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task JoinChannel(IPlayer player, int channelId);

    /// <summary>
    /// Removes a player from a channel.
    /// </summary>
    /// <param name="player">The player leaving.</param>
    /// <param name="channelId">The channel ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LeaveChannel(IPlayer player, int channelId);

    /// <summary>
    /// Checks whether a user is a member of a channel.
    /// </summary>
    /// <param name="channelId">The channel ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns><see langword="true"/> when a membership row exists, hidden rows included.</returns>
    Task<bool> UserInChannel(int channelId, int userId);

    /// <summary>
    /// Gets the IDs of the channels a user is a member of.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>The channel IDs.</returns>
    Task<IReadOnlyList<int>> GetUserJoinedChannels(int userId);

    /// <summary>
    /// Creates a message in a channel, persists it, and broadcasts it.
    /// </summary>
    /// <param name="channelId">The channel ID.</param>
    /// <param name="senderId">The sender user ID.</param>
    /// <param name="content">The message content.</param>
    /// <param name="isAction">Whether the message is an action (<c>/me</c>).</param>
    /// <param name="uuid">The optional client-generated UUID for deduplication.</param>
    /// <returns>The persisted message.</returns>
    /// <exception cref="ChatMessageRejectedException">
    /// Thrown when the message is blank, too long or sent too quickly.
    /// </exception>
    Task<ChatMessage> SendMessage(int channelId, int senderId, string content, bool isAction, string? uuid = null);

    /// <summary>
    /// Gets a channel by ID or name, creating it when needed for PM channels.
    /// </summary>
    /// <param name="channel">The channel ID or name.</param>
    /// <returns>The channel.</returns>
    Task<ChatChannel?> GetOrCreateChannel(string channel);

    /// <summary>
    /// Marks a message as read for a user in a channel.
    /// </summary>
    /// <param name="player">The player to mark as read.</param>
    /// <param name="channelId">The channel ID.</param>
    /// <param name="messageId">The message ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsRead(IPlayer player, int channelId, int messageId);

    /// <summary>
    /// Keeps the player's chat connection alive by refreshing the activity
    /// markers of every public channel they are in, mirroring osu-web's
    /// <c>Chat::ack</c>.
    /// </summary>
    /// <param name="player">The player to keep alive.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Ack(IPlayer player);

    /// <summary>
    /// Broadcasts a new private notification to its receivers.
    /// </summary>
    /// <param name="detail">The notification detail.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendNotification(NotificationDetail detail);
}