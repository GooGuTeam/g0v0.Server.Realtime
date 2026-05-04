// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user browsing the daily challenge lobby.
/// </summary>
public class InDailyChallengeLobbyActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.InDailyChallengeLobby;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return "In daily challenge lobby";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.InDailyChallengeLobby();
    }
}