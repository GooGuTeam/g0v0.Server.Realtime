// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Online.Spectator;

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Provides helper methods for spectator state values.
/// </summary>
public static class SpectatorStateExtensions
{
    /// <summary>
    /// Determines whether a spectator state represents an active play session.
    /// </summary>
    /// <param name="state">The spectator state.</param>
    /// <returns><see langword="true"/> if the player is playing or has just failed.</returns>
    public static bool IsPlaying(this SpectatorState state)
    {
        var userState = state.State;
        return userState is >= SpectatedUserState.Playing and <= SpectatedUserState.Failed;
    }
}