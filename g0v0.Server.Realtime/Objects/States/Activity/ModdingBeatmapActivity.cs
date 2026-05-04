// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user modding a beatmap.
/// </summary>
public class ModdingBeatmapActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.ModdingBeatmap;

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
        return $"Modding {BeatmapDisplayTitle}";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.ModdingBeatmap()
        {
            BeatmapID = BeatmapId,
            BeatmapDisplayTitle = BeatmapDisplayTitle,
        };
    }
}