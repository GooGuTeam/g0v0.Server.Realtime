// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user watching a replay.
/// </summary>
public class WatchingReplayActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.WatchingReplay;

    /// <summary>
    /// Gets or sets the replay score ID.
    /// </summary>
    public long ScoreID { get; set; }

    /// <summary>
    /// Gets or sets the replay owner's player name.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the beatmap ID.
    /// </summary>
    public int BeatmapID { get; set; }

    /// <summary>
    /// Gets or sets the beatmap display title.
    /// </summary>
    public string? BeatmapDisplayTitle { get; set; }

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return $"Watching {PlayerName}'s replay";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.WatchingReplay()
        {
            ScoreID = ScoreID,
            PlayerName = PlayerName,
            BeatmapID = BeatmapID,
            BeatmapDisplayTitle = BeatmapDisplayTitle,
        };
    }
}