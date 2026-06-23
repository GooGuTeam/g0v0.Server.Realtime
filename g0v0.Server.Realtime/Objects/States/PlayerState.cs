// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Online.Spectator;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States;

/// <summary>
/// Stores the current presence state for a connected player.
/// </summary>
/// <param name="userActivity">The initial user activity.</param>
public class PlayerState(IUserActivity userActivity)
{
    /// <summary>
    /// Gets or sets the current user activity.
    /// </summary>
    public IUserActivity UserActivity { get; set; } = userActivity;

    /// <summary>
    /// Gets or sets the current user status.
    /// </summary>
    public UserStatus? UserStatus { get; set; }

    /// <summary>
    /// Gets or sets the current spectator state, when the player is in a play session.
    /// </summary>
    public SpectatorState? SpectatorState { get; set; }

    /// <summary>
    /// Gets or sets the current score token, when the player is in a score-tracked play session.
    /// </summary>
    public long? ScoreToken { get; set; }

    /// <summary>
    /// Converts the current state into lazer presence payload format.
    /// </summary>
    /// <returns>The lazer presence payload, or <see langword="null"/> when the player should appear offline.</returns>
    public UserPresence? ToLazerUserPresence()
    {
        return UserStatus switch
        {
            null or osu.Game.Users.UserStatus.Offline => null,
            _ => new UserPresence { Activity = UserActivity.ToLazer(), Status = UserStatus, }
        };
    }
}