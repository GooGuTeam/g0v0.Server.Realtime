// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user testing a beatmap.
/// </summary>
public class TestingBeatmapActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.TestingBeatmap;

    /// <summary>
    /// Gets or sets the beatmap ID.
    /// </summary>
    public int BeatmapId { get; set; }

    /// <summary>
    /// Gets or sets the beatmap display title.
    /// </summary>
    public string BeatmapDisplayTitle { get; set; } = string.Empty;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return $"Testing {BeatmapDisplayTitle}";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.TestingBeatmap()
        {
            BeatmapID = BeatmapId,
            BeatmapDisplayTitle = BeatmapDisplayTitle,
        };
    }
}