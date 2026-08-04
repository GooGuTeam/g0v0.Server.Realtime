// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration.Attributes;

namespace g0v0.Server.Realtime;

/// <summary>
/// Stores realtime server behavior settings.
/// </summary>
[ConfigurationFile("realtime")]
public class RealtimeConfig
{
    /// <summary>
    /// The default maximum number of scores that can be started at the same time per spectator play session.
    /// </summary>
    public const int DefaultMaxStartedScores = 2;

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
}