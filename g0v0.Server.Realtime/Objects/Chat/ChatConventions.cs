// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using g0v0.Server.Common.Database.Models;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// Conventions shared by the chat endpoints and the notification server:
/// channel-name parsing, payload enum names and cache keys.
/// </summary>
internal static class ChatConventions
{
    /// <summary>
    /// Gets the title truncation limit of channel message notifications.
    /// </summary>
    internal const int MessageTitleTruncateLimit = 36;

    /// <summary>
    /// Gets the title truncation limit of mention notifications.
    /// </summary>
    internal const int MentionTitleTruncateLimit = 200;

    private const string PmPrefix = "pm_";

    private static readonly char[] PmNameSeparators = ['_', '-'];

    internal static string AckKey(int channelId) => $"chat:channel:{channelId}";

    internal static string ThrottleKey(int userId, bool isPrivate) => $"message_throttle:{userId}:{(isPrivate ? "PM" : "PUBLIC")}";

    internal static bool IsUserCommand(string content) =>
        content.Length > 1 && content[0] == '!' && content[1] != ' ' && content[1] != '!';

    /// <summary>
    /// Determines whether the message is a tournament management (<c>!mp</c>) command.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <returns><see langword="true"/> when the message is an <c>!mp</c> command.</returns>
    /// <remarks>
    /// Unlike other bot commands, <c>!mp</c> commands are part of the room
    /// conversation: they are delivered to every member of the channel so all
    /// participants see what the referee did.
    /// </remarks>
    internal static bool IsMultiplayerCommand(string content)
    {
        if (!IsUserCommand(content))
        {
            return false;
        }

        ReadOnlySpan<char> command = content.AsSpan(1);
        return command.Equals("mp", StringComparison.OrdinalIgnoreCase)
               || command.StartsWith("mp ", StringComparison.OrdinalIgnoreCase);
    }

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
    /// Determines whether a channel keeps its membership row when a user leaves.
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

    /// <summary>
    /// Truncates a message for a notification title: texts longer than the limit
    /// are cut and the ellipsis fills into the limit.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <param name="limit">The maximum length of the result.</param>
    /// <returns>The truncated content.</returns>
    internal static string Truncate(string content, int limit)
    {
        const string ellipsis = "...";

        return content.Length > limit
            ? string.Concat(content.AsSpan(0, limit - ellipsis.Length), ellipsis)
            : content;
    }

    /// <summary>
    /// Splits a message into the parts needed to stay within a channel's message
    /// length limit, so long bot replies are delivered instead of rejected.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <param name="limit">The maximum length of a single message.</param>
    /// <returns>
    /// The parts to send, in order, each no longer than <paramref name="limit"/>.
    /// Line breaks are preferred as split points, then spaces.
    /// </returns>
    /// <remarks>
    /// Lengths are counted in <see cref="char"/>s, which never exceeds the rune
    /// count <c>SendMessage</c> validates against, so every part is accepted.
    /// </remarks>
    internal static IReadOnlyList<string> SplitMessage(string content, int limit)
    {
        if (limit <= 0 || content.Length <= limit)
        {
            return [content];
        }

        List<string> parts = [];
        StringBuilder current = new();

        foreach (string piece in content.Split('\n').SelectMany(rawLine => SplitLine(rawLine.Trim(), limit)))
        {
            if (current.Length == 0)
            {
                current.Append(piece);
            }
            else if (current.Length + 1 + piece.Length <= limit)
            {
                current.Append(' ').Append(piece);
            }
            else
            {
                parts.Add(current.ToString());
                current.Clear();
                current.Append(piece);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts.Count == 0 ? [content] : parts;
    }

    /// <summary>
    /// Cuts one line into pieces that fit the limit, preferring spaces and never
    /// separating the two halves of a surrogate pair.
    /// </summary>
    /// <param name="line">The line to cut.</param>
    /// <param name="limit">The maximum length of a single piece.</param>
    /// <returns>The pieces of the line.</returns>
    private static IEnumerable<string> SplitLine(string line, int limit)
    {
        while (line.Length > limit)
        {
            int cut = line.LastIndexOf(' ', limit - 1);

            if (cut <= 0)
            {
                cut = char.IsHighSurrogate(line[limit - 1]) ? limit - 1 : limit;
            }

            yield return line[..cut].TrimEnd();

            line = line[cut..].TrimStart();
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }
}