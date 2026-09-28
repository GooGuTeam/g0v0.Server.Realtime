// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Maps notification names to their grouping categories, mirroring osu-web's
/// <c>Notification::NAME_TO_CATEGORY</c> and <c>Notification::namesInCategory()</c>,
/// and parses message mentions like <c>Message::mention()</c>.
/// </summary>
internal static partial class NotificationCategories
{
    private static readonly Dictionary<NotificationName, string> NameToCategory = new()
    {
        [NotificationName.BeatmapOwnerChange] = "beatmap_owner_change",
        [NotificationName.BeatmapsetDiscussionLock] = "beatmapset_discussion",
        [NotificationName.BeatmapsetDiscussionPostNew] = "beatmapset_discussion",
        [NotificationName.BeatmapsetDiscussionQualifiedProblem] = "beatmapset_problem",
        [NotificationName.BeatmapsetDiscussionReviewNew] = "beatmapset_discussion",
        [NotificationName.BeatmapsetDiscussionUnlock] = "beatmapset_discussion",
        [NotificationName.BeatmapsetDisqualify] = "beatmapset_state",
        [NotificationName.BeatmapsetLove] = "beatmapset_state",
        [NotificationName.BeatmapsetNominate] = "beatmapset_state",
        [NotificationName.BeatmapsetQualify] = "beatmapset_state",
        [NotificationName.BeatmapsetRank] = "beatmapset_state",
        [NotificationName.BeatmapsetRemoveFromLoved] = "beatmapset_state",
        [NotificationName.BeatmapsetResetNominations] = "beatmapset_state",
        [NotificationName.ChannelAnnouncement] = "announcement",
        [NotificationName.ChannelMessage] = "channel",
        [NotificationName.ChannelMention] = "channel_mention",
        [NotificationName.ChannelTeam] = "channel_team",
        [NotificationName.CommentNew] = "comment",
        [NotificationName.ForumTopicReply] = "forum_topic_reply",
        [NotificationName.TeamApplicationAccept] = "team_application",
        [NotificationName.TeamApplicationReject] = "team_application",
        [NotificationName.TeamApplicationStore] = "team_application",
        [NotificationName.UserAchievementUnlock] = "user_achievement_unlock",
        [NotificationName.UserBeatmapsetNew] = "user_beatmapset_new",
        [NotificationName.UserBeatmapsetRevive] = "user_beatmapset_new",
    };

    /// <summary>
    /// Gets the database name of a notification name, the snake-cased osu-web
    /// constant value (for example <c>channel_message</c>).
    /// </summary>
    /// <param name="name">The notification name.</param>
    /// <returns>The database value.</returns>
    internal static string ToDatabaseName(NotificationName name)
    {
        StringBuilder builder = new();
        string camel = name.ToString();

        for (int index = 0; index < camel.Length; index++)
        {
            if (index > 0 && char.IsUpper(camel[index]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(camel[index]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Gets the category of a notification name.
    /// </summary>
    /// <param name="name">The notification name.</param>
    /// <returns>The category.</returns>
    internal static string CategoryOf(NotificationName name)
    {
        return NameToCategory.TryGetValue(name, out string? category) ? category : ToDatabaseName(name);
    }

    /// <summary>
    /// Gets every notification name belonging to a category, mirroring
    /// osu-web's <c>Notification::namesInCategory()</c>.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The notification names.</returns>
    internal static IReadOnlyList<string> NamesInCategory(string category)
    {
        List<string> names = NameToCategory
            .Where(pair => string.Equals(pair.Value, category, StringComparison.Ordinal))
            .Select(pair => ToDatabaseName(pair.Key))
            .ToList();

        return names.Count > 0 ? names : [category];
    }

    /// <summary>
    /// Extracts the username mentioned in a message, mirroring osu-web's
    /// <c>Message::mention()</c> together with
    /// <c>UsernamesForDbLookup::make(trimPrefix: true)</c>: the first
    /// <c>@username</c> token and its spacing variants are considered.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <returns>The username candidates, empty when nothing valid is mentioned.</returns>
    internal static IReadOnlyList<string> MentionUsernames(string content)
    {
        Match match = MentionRegex().Match(content);
        if (!match.Success)
        {
            return [];
        }

        string username = match.Groups["username"].Value;

        return username.Length is < 3 or > 15
            ? []
            : [username, username.Replace(' ', '_'), username.Replace('_', ' ')];
    }

    [GeneratedRegex(@"(?<=^|[\s'"",./])@(?<username>[A-Za-z0-9\-[\]_]+)", RegexOptions.ExplicitCapture, 200)]
    private static partial Regex MentionRegex();
}