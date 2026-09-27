// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Notification names, see <see href="https://github.com/ppy/osu-web/blob/master/app/Models/Notification.php"/>.
/// </summary>
[JsonConverter(typeof(NotificationNameJsonConverter))]
public enum NotificationName
{
    /// <summary>
    /// A beatmap's owner changed.
    /// </summary>
    BeatmapOwnerChange,

    /// <summary>
    /// A beatmapset discussion was locked.
    /// </summary>
    BeatmapsetDiscussionLock,

    /// <summary>
    /// A new beatmapset discussion post was created.
    /// </summary>
    BeatmapsetDiscussionPostNew,

    /// <summary>
    /// A beatmapset discussion reported a qualified problem.
    /// </summary>
    BeatmapsetDiscussionQualifiedProblem,

    /// <summary>
    /// A new beatmapset discussion review was created.
    /// </summary>
    BeatmapsetDiscussionReviewNew,

    /// <summary>
    /// A beatmapset discussion was unlocked.
    /// </summary>
    BeatmapsetDiscussionUnlock,

    /// <summary>
    /// A beatmapset was disqualified.
    /// </summary>
    BeatmapsetDisqualify,

    /// <summary>
    /// A beatmapset was loved.
    /// </summary>
    BeatmapsetLove,

    /// <summary>
    /// A beatmapset was nominated.
    /// </summary>
    BeatmapsetNominate,

    /// <summary>
    /// A beatmapset was qualified.
    /// </summary>
    BeatmapsetQualify,

    /// <summary>
    /// A beatmapset was ranked.
    /// </summary>
    BeatmapsetRank,

    /// <summary>
    /// A beatmapset was removed from loved.
    /// </summary>
    BeatmapsetRemoveFromLoved,

    /// <summary>
    /// A beatmapset's nominations were reset.
    /// </summary>
    BeatmapsetResetNominations,

    /// <summary>
    /// A channel announcement was made.
    /// </summary>
    ChannelAnnouncement,

    /// <summary>
    /// A channel message was sent.
    /// </summary>
    ChannelMessage,

    /// <summary>
    /// A team channel message was sent.
    /// </summary>
    ChannelTeam,

    /// <summary>
    /// A new comment was created.
    /// </summary>
    CommentNew,

    /// <summary>
    /// A forum topic received a reply.
    /// </summary>
    ForumTopicReply,

    /// <summary>
    /// A team application was accepted.
    /// </summary>
    TeamApplicationAccept,

    /// <summary>
    /// A team application was rejected.
    /// </summary>
    TeamApplicationReject,

    /// <summary>
    /// A team application was stored.
    /// </summary>
    TeamApplicationStore,

    /// <summary>
    /// A user achievement was unlocked.
    /// </summary>
    UserAchievementUnlock,

    /// <summary>
    /// A user created a new beatmapset.
    /// </summary>
    UserBeatmapsetNew,

    /// <summary>
    /// A user's beatmapset was revived.
    /// </summary>
    UserBeatmapsetRevive,
}