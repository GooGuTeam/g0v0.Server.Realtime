// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user spectating another player.
/// </summary>
public class SpectatingUserActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.SpectatingUser;

    /// <summary>
    /// Gets or sets the spectated score ID.
    /// </summary>
    public long ScoreID { get; set; }

    /// <summary>
    /// Gets or sets the spectated player's name.
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
        return $"Spectating {PlayerName}";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.SpectatingUser()
        {
            ScoreID = ScoreID,
            PlayerName = PlayerName,
            BeatmapID = BeatmapID,
            BeatmapDisplayTitle = BeatmapDisplayTitle,
        };
    }
}