// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.Players;

/// <summary>
/// Represents a connected realtime player.
/// </summary>
public interface IPlayer
{
    /// <summary>
    /// Gets the numeric player ID.
    /// </summary>
    int PlayerId { get; }

    /// <summary>
    /// Gets the source server identifier for the connection.
    /// </summary>
    string Server { get; }

    /// <summary>
    /// Gets the current player state.
    /// </summary>
    PlayerState State { get; }

    /// <summary>
    /// Handles another player coming online.
    /// </summary>
    /// <param name="player">The player that came online.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnPlayerOnline(IPlayer player);

    /// <summary>
    /// Handles another player going offline.
    /// </summary>
    /// <param name="player">The player that went offline.</param>
    /// <param name="isKicked">Whether the player was kicked because a replacement connection was opened.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnPlayerOffline(IPlayer player, bool isKicked = false);

    /// <summary>
    /// Handles another player changing activity.
    /// </summary>
    /// <param name="player">The player that changed activity.</param>
    /// <param name="activity">The updated activity.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnPlayerChangeActivity(IPlayer player, IUserActivity activity);

    /// <summary>
    /// Handles another player changing status.
    /// </summary>
    /// <param name="player">The player that changed status.</param>
    /// <param name="status">The updated status.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnPlayerChangeStatus(IPlayer player, UserStatus? status);

    /// <summary>
    /// Marks the player as online.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Online();

    /// <summary>
    /// Marks the player as offline.
    /// </summary>
    /// <param name="isKicked">Whether the player was kicked because a replacement connection was opened.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Offline(bool isKicked = false);

    /// <summary>
    /// Changes the player's current activity.
    /// </summary>
    /// <param name="newActivity">The new activity.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ChangePlayerActivityAsync(IUserActivity newActivity);

    /// <summary>
    /// Changes the player's current status.
    /// </summary>
    /// <param name="newStatus">The new status.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ChangePlayerStatusAsync(UserStatus? newStatus);
}