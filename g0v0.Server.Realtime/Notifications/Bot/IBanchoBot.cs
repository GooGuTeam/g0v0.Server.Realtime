// Copyright (c) GooGuTeam. License under MIT License.See LICENSE in the project root for license information.

using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Notifications.Bot;

/// <summary>
/// Handles chat bot commands prefixed with <c>!</c>.
/// </summary>
public interface IBanchoBot
{
    /// <summary>
    /// The user ID of the bot account.
    /// </summary>
    public const int BanchoBotId = 2;

    /// <summary>
    /// Attempts to parse and handle a message as a bot command.
    /// </summary>
    /// <param name="senderId">The user ID of the sender.</param>
    /// <param name="channelId">The channel the message was sent in.</param>
    /// <param name="content">The message content.</param>
    /// <param name="channelType">The type of the channel the message was sent in.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task TryHandleAsync(int senderId, int channelId, string content, ChannelType channelType);
}