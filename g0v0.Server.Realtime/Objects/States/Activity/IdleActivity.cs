// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents an idle user.
/// </summary>
public class IdleActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.Idle;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return "Idle";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return null;
    }
}