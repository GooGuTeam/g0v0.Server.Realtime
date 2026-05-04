// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.Players;

/// <summary>
/// Provides shared state and lifecycle handling for connected players.
/// </summary>
/// <param name="playerId">The player ID.</param>
/// <param name="manager">The player manager.</param>
/// <param name="state">The initial player state.</param>
public abstract class PlayerBase(int playerId, PlayerManager manager, PlayerState? state = null) : IPlayer
{
    /// <inheritdoc />
    public int PlayerId { get; } = playerId;

    /// <inheritdoc />
    public abstract string Server { get; }

    /// <inheritdoc />
    public PlayerState State { get; } = state ?? new PlayerState(new IdleActivity());

    /// <inheritdoc />
    public abstract Task OnPlayerOnline(IPlayer player);

    /// <inheritdoc />
    public abstract Task OnPlayerOffline(IPlayer player, bool isKicked = false);

    /// <inheritdoc />
    public abstract Task OnPlayerChangeActivity(IPlayer player, IUserActivity activity);

    /// <inheritdoc />
    public abstract Task OnPlayerChangeStatus(IPlayer player, UserStatus? status);

    /// <inheritdoc />
    public async Task Online()
    {
        await manager.AddPlayer(this);
    }

    /// <inheritdoc />
    public async Task Offline(bool isKicked = false)
    {
        await manager.RemovePlayer(this, isKicked);
    }

    /// <inheritdoc />
    public async Task ChangePlayerActivityAsync(IUserActivity newActivity)
    {
        if (newActivity == State.UserActivity)
        {
            return;
        }

        State.UserActivity = newActivity;
        await manager.BroadcastPlayerChangeActivity(this, newActivity);
    }

    /// <inheritdoc />
    public async Task ChangePlayerStatusAsync(UserStatus? newStatus)
    {
        if (newStatus == State.UserStatus)
        {
            return;
        }

        State.UserStatus = newStatus;
        await manager.BroadcastPlayerChangeStatus(this, newStatus);
    }
}