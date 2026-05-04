// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user choosing a beatmap.
/// </summary>
public class ChoosingBeatmapActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.ChoosingBeatmap;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return "Choosing Beatmap";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.ChoosingBeatmap();
    }
}