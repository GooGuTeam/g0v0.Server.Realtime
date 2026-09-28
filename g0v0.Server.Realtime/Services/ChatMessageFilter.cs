// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Realtime.Objects.Chat;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Applies the message filter to a channel's history.
/// </summary>
internal static class ChatMessageFilter
{
    /// <summary>
    /// Filters the messages of a channel: public channels hide anything older
    /// than the backlog limit, and other users' bancho commands never reach the
    /// visible history.
    /// </summary>
    /// <param name="messages">The messages to filter, oldest first.</param>
    /// <param name="channel">The channel the messages belong to.</param>
    /// <param name="requestingUserId">The user the history is built for.</param>
    /// <param name="publicBacklogLimit">The age limit applied to public channels.</param>
    /// <returns>The visible messages.</returns>
    internal static IEnumerable<ChatMessage> Apply(
        IReadOnlyList<ChatMessage> messages,
        ChatChannel channel,
        int requestingUserId,
        TimeSpan publicBacklogLimit)
    {
        DateTimeOffset minTimestamp = DateTimeOffset.UtcNow - publicBacklogLimit;

        return messages
            .Where(message => channel.Type != ChannelType.Public || message.Timestamp >= minTimestamp)
            .Where(message => message.SenderId == requestingUserId || !ChatConventions.IsUserCommand(message.Content));
    }
}