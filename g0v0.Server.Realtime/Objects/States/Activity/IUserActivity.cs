// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents the activity of a user, independent of the client (stable/lazer).
/// </summary>
public interface IUserActivity
{
    /// <summary>
    /// Gets the normalized realtime activity type.
    /// </summary>
    UserActivityType Type { get; }

    /// <summary>
    /// Gets a human-readable activity description.
    /// </summary>
    /// <returns>The display text.</returns>
    string GetDisplayText();

    /// <summary>
    /// Converts the realtime activity into the lazer activity payload format.
    /// </summary>
    /// <returns>The lazer activity payload, or <see langword="null"/> when no lazer activity should be sent.</returns>
    osu.Game.Users.UserActivity? ToLazer();
}