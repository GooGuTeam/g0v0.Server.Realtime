// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents an AFK (away from keyboard) activity for osu!stable compatibility.
/// </summary>
public class AfkActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.Afk;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return "AFK";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return null;
    }
}