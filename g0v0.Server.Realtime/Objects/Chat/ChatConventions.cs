// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// Conventions shared by the chat endpoints and the notification server:
/// channel-name parsing, payload enum names and cache keys.
/// </summary>
internal static class ChatConventions
{
    private const string PmPrefix = "pm_";

    private static readonly char[] PmNameSeparators = ['_', '-'];

    /// <summary>
    /// Gets the key of the activity markers of a public channel, mirroring
    /// osu-web's <c>Channel::getAckKey</c>.
    /// </summary>
    /// <param name="channelId">The channel ID.</param>
    /// <returns>The cache key.</returns>
    internal static string AckKey(int channelId) => $"chat:channel:{channelId}";

    /// <summary>
    /// Gets the key of a user's message throttle bucket, mirroring osu-web's
    /// <c>message_throttle:{userId}:{kind}</c> key.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="isPrivate">Whether the bucket tracks a private (PM) channel.</param>
    /// <returns>The cache key.</returns>
    internal static string ThrottleKey(int userId, bool isPrivate) => $"message_throttle:{userId}:{(isPrivate ? "PM" : "PUBLIC")}";

    /// <summary>
    /// Determines whether a message is a BanchoBot command, mirroring osu-web's
    /// <c>Message::isUserCommand()</c> (<c>/^![^ !]/</c>).
    /// </summary>
    /// <param name="content">The raw message content.</param>
    /// <returns><see langword="true"/> when the content is a user command.</returns>
    internal static bool IsUserCommand(string content) =>
        content.Length > 1 && content[0] == '!' && content[1] != ' ' && content[1] != '!';

    /// <summary>
    /// Gets the wire name of a channel type, mirroring osu-web's <c>Channel::TYPES</c>.
    /// </summary>
    /// <param name="type">The channel type.</param>
    /// <returns>The upper-case type name used by the API.</returns>
    internal static string GetChannelTypeName(ChannelType type)
    {
        return type switch
        {
            ChannelType.Public => "PUBLIC",
            ChannelType.Private => "PRIVATE",
            ChannelType.Multiplayer => "MULTIPLAYER",
            ChannelType.Spectator => "SPECTATOR",
            ChannelType.Temporary => "TEMPORARY",
            ChannelType.PM => "PM",
            ChannelType.Group => "GROUP",
            ChannelType.System => "SYSTEM",
            ChannelType.Announce => "ANNOUNCE",
            ChannelType.Team => "TEAM",
            _ => type.ToString().ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Gets the wire name of a message type, mirroring osu-web's
    /// <c>MessageTransformer</c>: actions are <c>action</c>, announcement
    /// channel messages are <c>markdown</c> and everything else is <c>plain</c>.
    /// </summary>
    /// <param name="type">The message type.</param>
    /// <param name="isAnnouncement">Whether the message was posted in an announcement channel.</param>
    /// <returns>The type name used by the API.</returns>
    internal static string GetMessageTypeName(MessageType type, bool isAnnouncement)
    {
        return type switch
        {
            MessageType.Action => "action",
            MessageType.Markdown => "markdown",
            _ => isAnnouncement ? "markdown" : "plain",
        };
    }

    /// <summary>
    /// Determines whether a channel keeps its membership row when a user leaves,
    /// mirroring osu-web's <c>Channel::isHideable()</c>.
    /// </summary>
    /// <param name="type">The channel type.</param>
    /// <returns><see langword="true"/> when leaving the channel only hides it.</returns>
    internal static bool IsHideable(ChannelType type) => type is ChannelType.PM or ChannelType.Announce;

    /// <summary>
    /// Parses the two user IDs of a PM channel name, accepting the
    /// <c>pm_1_2</c> and <c>#pm_1-2</c> conventions.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="userIds">The parsed user IDs, in name order.</param>
    /// <returns><see langword="true"/> when the name is a PM channel name.</returns>
    internal static bool TryGetPmUserIds(string channelName, out int[] userIds)
    {
        userIds = [];
        string normalizedName = channelName.StartsWith('#')
            ? channelName[1..]
            : channelName;

        if (!normalizedName.StartsWith(PmPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = normalizedName[PmPrefix.Length..].Split(PmNameSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int firstUserId) || !int.TryParse(parts[1], out int secondUserId))
        {
            return false;
        }

        userIds = [firstUserId, secondUserId];
        return true;
    }
}