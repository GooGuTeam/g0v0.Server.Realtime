// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user editing a beatmap.
/// </summary>
public class EditingBeatmapActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.EditingBeatmap;

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
        return $"Editing {BeatmapDisplayTitle}";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.EditingBeatmap()
        {
            BeatmapID = BeatmapId,
            BeatmapDisplayTitle = BeatmapDisplayTitle,
        };
    }
}