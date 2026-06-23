// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;
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

    #region Online Status

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

    #endregion

    #region Spectator

    /// <summary>
    /// Handles a watched player beginning a play session.
    /// </summary>
    /// <param name="player">The player that began playing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnUserBeganPlaying(IPlayer player);

    /// <summary>
    /// Handles a watched player finishing a play session.
    /// </summary>
    /// <param name="player">The player that finished playing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnUserFinishedPlaying(IPlayer player);

    /// <summary>
    /// Handles spectator frame data from a watched player.
    /// </summary>
    /// <param name="player">The player that sent frame data.</param>
    /// <param name="frame">The frame data bundle.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnUserSentFrames(IPlayer player, FrameDataBundle frame);

    /// <summary>
    /// Handles another player starting to watch this player.
    /// </summary>
    /// <param name="source">The watcher.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnWatched(IPlayer source);

    /// <summary>
    /// Handles another player stopping watching this player.
    /// </summary>
    /// <param name="source">The watcher.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnWatchedStopped(IPlayer source);

    /// <summary>
    /// Called when a score has been fully processed by the lazer server.
    /// </summary>
    /// <param name="scoreId">The id of the processed score.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnScoreProcessed(long scoreId);

    /// <summary>
    /// Begins the player's spectator play session.
    /// </summary>
    /// <param name="scoreToken">The score token associated with the session, if any.</param>
    /// <param name="score">The initial score object to buffer.</param>
    /// <param name="spectatorState">The spectator state describing the session.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task BeingPlaying(long? scoreToken, Score score, SpectatorState spectatorState);

    /// <summary>
    /// Finishes the player's spectator play session.
    /// </summary>
    /// <param name="spectatorState">The final spectator state.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task FinishPlaying(SpectatorState spectatorState);

    /// <summary>
    /// Sends spectator frame data for the current play session.
    /// </summary>
    /// <param name="data">The frame data bundle.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendFrames(FrameDataBundle data);

    /// <summary>
    /// Starts watching another player.
    /// </summary>
    /// <param name="target">The player to watch.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task WatchPlayer(IPlayer target);

    /// <summary>
    /// Stops watching another player.
    /// </summary>
    /// <param name="target">The player to stop watching.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopWatchPlayer(IPlayer target);

    #endregion
}