// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Online;
using osu.Game.Online.Multiplayer;

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Provides state helpers for multiplayer user states and room users.
/// </summary>
public static class MultiplayerUserStateExtensions
{
    /// <summary>
    /// Determines whether the state is a gameplay state (loading, loaded, ready or playing).
    /// </summary>
    /// <param name="state">
    /// The state to check.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the state is a gameplay state.
    /// </returns>
    public static bool IsGameplayState(this MultiplayerUserState state)
    {
        return state switch
        {
            MultiplayerUserState.WaitingForLoad
                or MultiplayerUserState.Loaded
                or MultiplayerUserState.ReadyForGameplay
                or MultiplayerUserState.Playing => true,
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether the user is in a state capable of starting gameplay.
    /// </summary>
    /// <param name="user">
    /// The user to check.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the user is ready for gameplay.
    /// </returns>
    public static bool IsReadyForGameplay(this MultiplayerRoomUser user)
        => user is { Role: MultiplayerRoomUserRole.Player, BeatmapAvailability.State: DownloadState.LocallyAvailable, State: MultiplayerUserState.Ready or MultiplayerUserState.Idle };
}