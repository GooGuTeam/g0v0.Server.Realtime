// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration.Attributes;

namespace g0v0.Server.Realtime;

/// <summary>
/// Stores chat limits, mirroring the <c>chat</c> section of osu-web's <c>config/osu.php</c>.
/// </summary>
/// <remarks>
/// The file is optional: when <c>config/chat.json</c> is absent every value
/// falls back to the upstream default.
/// </remarks>
[ConfigFile("chat")]
public class ChatConfig
{
    /// <summary>
    /// The default maximum length of a chat message.
    /// </summary>
    public const int DefaultMessageLengthLimit = 450;

    /// <summary>
    /// The default maximum length of an announcement message.
    /// </summary>
    public const int DefaultAnnounceMessageLengthLimit = 1024;

    /// <summary>
    /// The default number of hours of history exposed for public channels.
    /// </summary>
    public const int DefaultPublicBacklogLimitHours = 24;

    /// <summary>
    /// The default number of seconds a user stays active in a public channel
    /// after acknowledging it.
    /// </summary>
    public const int DefaultActivityTimeoutSeconds = 60;

    /// <summary>
    /// Gets or sets the maximum number of channels listed for a single user.
    /// </summary>
    [Reloadable]
    public int ChannelLimit { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the maximum length of a chat message.
    /// </summary>
    [Reloadable]
    public int MessageLengthLimit { get; set; } = DefaultMessageLengthLimit;

    /// <summary>
    /// Gets or sets the maximum length of an announcement message.
    /// </summary>
    [Reloadable]
    public int AnnounceMessageLengthLimit { get; set; } = DefaultAnnounceMessageLengthLimit;

    /// <summary>
    /// Gets or sets the number of hours of history exposed for public channels.
    /// </summary>
    [Reloadable]
    public int PublicBacklogLimitHours { get; set; } = DefaultPublicBacklogLimitHours;

    /// <summary>
    /// Gets or sets the number of seconds a user stays active in a public
    /// channel after acknowledging it, mirroring <c>Channel::CHAT_ACTIVITY_TIMEOUT</c>.
    /// </summary>
    [Reloadable]
    public int ActivityTimeoutSeconds { get; set; } = DefaultActivityTimeoutSeconds;

    /// <summary>
    /// Gets or sets the number of recent messages attached to a channel.
    /// </summary>
    [Reloadable]
    public int RecentMessageLimit { get; set; } = 50;

    /// <summary>
    /// Gets or sets the number of messages a user may post in a public channel per window.
    /// </summary>
    [Reloadable]
    public int PublicMessageLimit { get; set; } = 1;

    /// <summary>
    /// Gets or sets the public message rate limit window, in seconds.
    /// </summary>
    [Reloadable]
    public int PublicMessageWindowSeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number of messages a user may post in a private channel per window.
    /// </summary>
    [Reloadable]
    public int PrivateMessageLimit { get; set; } = 1;

    /// <summary>
    /// Gets or sets the private message rate limit window, in seconds.
    /// </summary>
    [Reloadable]
    public int PrivateMessageWindowSeconds { get; set; } = 1;

    /// <summary>
    /// Gets the time-to-live of the activity markers of a public channel,
    /// mirroring osu-web's <c>CHAT_ACTIVITY_TIMEOUT * 10</c>.
    /// </summary>
    public TimeSpan ActivityMarkerExpiry => TimeSpan.FromSeconds(ActivityTimeoutSeconds * 10);

    /// <summary>
    /// Gets the age limit of the history exposed for public channels.
    /// </summary>
    public TimeSpan PublicBacklogLimit => TimeSpan.FromHours(PublicBacklogLimitHours);
}