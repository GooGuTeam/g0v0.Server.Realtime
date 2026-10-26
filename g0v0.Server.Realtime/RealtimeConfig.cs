// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration.Attributes;

namespace g0v0.Server.Realtime;

/// <summary>
/// Stores realtime server behavior settings.
/// </summary>
[ConfigFile("realtime")]
public class RealtimeConfig
{
    /// <summary>
    /// The default maximum number of scores that can be started at the same time per spectator play session.
    /// </summary>
    public const int DefaultMaxStartedScores = 2;

    /// <summary>
    /// The default maximum number of playlist items a host may enqueue.
    /// </summary>
    public const int DefaultHostPlaylistLimit = 50;

    /// <summary>
    /// The default maximum number of playlist items a guest may enqueue.
    /// </summary>
    public const int DefaultGuestPlaylistLimit = 3;

    /// <summary>
    /// Gets or sets a value indicating whether spectator replays should be persisted.
    /// </summary>
    [Reloadable]
    public bool SaveReplays { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of scores that can be started at the same time per spectator play session.
    /// </summary>
    [Reloadable]
    public int MaxStartedScores { get; set; } = DefaultMaxStartedScores;

    /// <summary>
    /// Gets or sets the number of concurrent replay uploader workers.
    /// </summary>
    public int ReplayUploaderConcurrency { get; set; } = 4;

    /// <summary>
    /// Gets or sets the limit of playlist items of the host.
    /// </summary>
    public int HostPlaylistLimit { get; set; } = DefaultHostPlaylistLimit;

    /// <summary>
    /// Gets or sets the limit of playlist items of the guest.
    /// </summary>
    public int GuestPlaylistLimit { get; set; } = DefaultGuestPlaylistLimit;

    /// <summary>
    /// Gets or sets the grace period during which a disconnected user is kept in
    /// their room before being removed, allowing a brief reconnect.
    /// </summary>
    [Reloadable]
    public TimeSpan MultiplayerDisconnectGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the period for which an empty tournament room is retained
    /// before being destroyed.
    /// </summary>
    [Reloadable]
    public TimeSpan TournamentRoomRetentionPeriod { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets the number of tournament rooms a single user may own at the
    /// same time through <c>!mp make</c>.
    /// </summary>
    [Reloadable]
    public int MaxActiveTournamentRooms { get; set; } = 4;

    /// <summary>
    /// Gets or sets the Sentry error-monitoring settings.
    /// </summary>
    public SentryOptions Sentry { get; set; } = new();
}