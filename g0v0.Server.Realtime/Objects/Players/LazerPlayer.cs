// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Online.Spectator;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.Players;

/// <summary>
/// Represents a lazer client connected to the realtime server.
/// </summary>
/// <param name="playerId">The player ID.</param>
/// <param name="facade">The player dependency facade.</param>
public class LazerPlayer(int playerId, IPlayerFacade facade) : PlayerBase(playerId, facade)
{
    private readonly object _hubConnectionLock = new();

    private bool _metadataConnected;
    private bool _spectatorConnected;
    private bool _multiplayerConnected;

    /// <inheritdoc />
    public override string Server => "Lazer";

    #region MetadataHub

    /// <summary>
    /// Gets or sets the current friend IDs tracked by the metadata hub connection.
    /// </summary>
    public int[] FriendIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the metadata hub online callback.
    /// </summary>
    public Func<IPlayer, Task>? OnPlayerOnlineForMetadataHub { get; set; }

    /// <summary>
    /// Gets or sets the metadata hub offline callback.
    /// </summary>
    public Func<IPlayer, bool, Task>? OnPlayerOfflineForMetadataHub { get; set; }

    /// <summary>
    /// Gets or sets the metadata hub activity change callback.
    /// </summary>
    public Func<IPlayer, IUserActivity, Task>? OnPlayerChangeActivityForMetadataHub { get; set; }

    /// <summary>
    /// Gets or sets the metadata hub status change callback.
    /// </summary>
    public Func<IPlayer, UserStatus?, Task>? OnPlayerChangeStatusForMetadataHub { get; set; }

    #endregion

    #region SpectatorHub

    /// <summary>
    /// Gets or sets the spectator hub begin-play callback.
    /// </summary>
    public Func<IPlayer, Task>? OnUserBeganPlayingForSpectatorHub { get; set; }

    /// <summary>
    /// Gets or sets the spectator hub finish-play callback.
    /// </summary>
    public Func<IPlayer, Task>? OnUserFinishedPlayingForSpectatorHub { get; set; }

    /// <summary>
    /// Gets or sets the spectator hub frame callback.
    /// </summary>
    public Func<IPlayer, FrameDataBundle, Task>? OnUserSentFramesForSpectatorHub { get; set; }

    /// <summary>
    /// Gets or sets the spectator hub watcher-added callback.
    /// </summary>
    public Func<IPlayer, Task>? OnWatchedForSpectatorHub { get; set; }

    /// <summary>
    /// Gets or sets the spectator hub watcher-removed callback.
    /// </summary>
    public Func<IPlayer, Task>? OnWatchedStoppedForSpectatorHub { get; set; }

    /// <summary>
    /// Gets or sets the spectator hub score-processed callback.
    /// </summary>
    public Func<long, Task>? OnScoreProcessedForSpectatorHub { get; set; }

    #endregion

    private bool IsConnectedToAnyHub => _metadataConnected || _spectatorConnected || _multiplayerConnected;

    /// <inheritdoc />
    public override Task OnPlayerOnline(IPlayer player)
    {
        if (OnPlayerOnlineForMetadataHub != null)
        {
            return OnPlayerOnlineForMetadataHub(player);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnPlayerOffline(IPlayer player, bool isKicked = false)
    {
        if (OnPlayerOfflineForMetadataHub != null)
        {
            return OnPlayerOfflineForMetadataHub(player, isKicked);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnPlayerChangeActivity(IPlayer player, IUserActivity activity)
    {
        if (OnPlayerChangeActivityForMetadataHub != null)
        {
            return OnPlayerChangeActivityForMetadataHub(player, activity);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnPlayerChangeStatus(IPlayer player, UserStatus? status)
    {
        if (OnPlayerChangeStatusForMetadataHub != null)
        {
            return OnPlayerChangeStatusForMetadataHub(player, status);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnUserBeganPlaying(IPlayer player)
    {
        if (OnUserBeganPlayingForSpectatorHub != null)
        {
            return OnUserBeganPlayingForSpectatorHub(player);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnUserFinishedPlaying(IPlayer player)
    {
        if (OnUserFinishedPlayingForSpectatorHub != null)
        {
            return OnUserFinishedPlayingForSpectatorHub(player);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnUserSentFrames(IPlayer player, FrameDataBundle frame)
    {
        if (OnUserSentFramesForSpectatorHub != null)
        {
            return OnUserSentFramesForSpectatorHub(player, frame);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnWatched(IPlayer source)
    {
        if (OnWatchedForSpectatorHub != null)
        {
            return OnWatchedForSpectatorHub(source);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnWatchedStopped(IPlayer source)
    {
        if (OnWatchedStoppedForSpectatorHub != null)
        {
            return OnWatchedStoppedForSpectatorHub(source);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task OnScoreProcessed(long scoreId)
    {
        if (OnScoreProcessedForSpectatorHub != null)
        {
            return OnScoreProcessedForSpectatorHub(scoreId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Marks a specific hub as connected for this player.
    /// </summary>
    /// <param name="hubName">The connected hub name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HubConnected(string hubName)
    {
        bool shouldGoOnline;
        lock (_hubConnectionLock)
        {
            shouldGoOnline = !IsConnectedToAnyHub;

            switch (hubName)
            {
                case "MetadataHub":
                    _metadataConnected = true;
                    break;
                case "SpectatorHub":
                    _spectatorConnected = true;
                    break;
                case "MultiplayerHub":
                    _multiplayerConnected = true;
                    break;
            }
        }

        if (shouldGoOnline)
        {
            await Online();
        }
    }

    /// <summary>
    /// Marks a specific hub as disconnected for this player and removes the player when no realtime hubs remain.
    /// </summary>
    /// <param name="hubName">The disconnected hub name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HubDisconnected(string hubName)
    {
        bool shouldGoOffline;
        lock (_hubConnectionLock)
        {
            switch (hubName)
            {
                case "MetadataHub":
                    _metadataConnected = false;
                    break;
                case "SpectatorHub":
                    _spectatorConnected = false;
                    break;
                case "MultiplayerHub":
                    _multiplayerConnected = false;
                    break;
            }

            shouldGoOffline = !IsConnectedToAnyHub;
        }

        if (shouldGoOffline)
        {
            await Offline(isKicked: false);
        }
    }
}