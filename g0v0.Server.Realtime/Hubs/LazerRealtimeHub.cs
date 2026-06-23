// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using Microsoft.AspNetCore.SignalR;

namespace g0v0.Server.Realtime.Hubs;

/// <summary>
/// Provides common realtime hub functionality for lazer clients.
/// </summary>
/// <typeparam name="T">The hub client contract.</typeparam>
public class LazerRealtimeHub<T>(PlayerManager playerManager) : Hub<T>
    where T : class
{
    /// <summary>
    /// Gets the player manager used by the hub.
    /// </summary>
    protected PlayerManager PlayerManager => playerManager;

    /// <summary>
    /// Gets the currently connected lazer player.
    /// </summary>
    /// <returns>The current lazer player.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the current connection has no registered player.</exception>
    protected LazerPlayer GetPlayer()
    {
        var player = playerManager.GetPlayer<LazerPlayer>(Context.GetUserId());

        Debug.Assert(player != null, "player != null");
        return player ??
               throw new InvalidOperationException(
                   "The current connection is not associated with an active lazer player.");
    }

    /// <summary>
    /// Gets the current lazer player or creates it with the supplied facade.
    /// </summary>
    /// <param name="facade">The facade to use when creating or updating the player.</param>
    /// <returns>The current lazer player.</returns>
    protected LazerPlayer GetOrCreatePlayer(IPlayerFacade facade)
    {
        return playerManager.GetOrCreatePlayer(
            Context.GetUserId(),
            "Lazer",
            () => new LazerPlayer(Context.GetUserId(), facade),
            player => player.Facade.ApplyNonNullDependenciesFrom(facade));
    }
}