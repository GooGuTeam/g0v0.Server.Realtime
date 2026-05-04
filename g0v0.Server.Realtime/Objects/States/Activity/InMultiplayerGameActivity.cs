// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user playing a multiplayer game.
/// </summary>
public class InMultiplayerGameActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.InMultiplayerGame;

    /// <summary>
    /// Gets or sets the beatmap ID.
    /// </summary>
    public int BeatmapId { get; set; }

    /// <summary>
    /// Gets or sets the beatmap display title.
    /// </summary>
    public string BeatmapDisplayTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ruleset ID.
    /// </summary>
    public int RulesetId { get; set; }

    /// <summary>
    /// Gets or sets the verb describing the current play action.
    /// </summary>
    public string RulesetPlayingVerb { get; set; } = string.Empty;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return $"{RulesetPlayingVerb} with others";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.InMultiplayerGame()
        {
            BeatmapID = BeatmapId,
            BeatmapDisplayTitle = BeatmapDisplayTitle,
            RulesetID = RulesetId,
            RulesetPlayingVerb = RulesetPlayingVerb,
        };
    }
}