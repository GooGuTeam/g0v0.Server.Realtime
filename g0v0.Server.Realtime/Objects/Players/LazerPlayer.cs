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

    private string _metadataConnectionId = string.Empty;
    private string _spectatorConnectionId = string.Empty;
    private string _multiplayerConnectionId = string.Empty;

    /// <inheritdoc />
    public override string Server => "Lazer";

    #region MetadataHub

    /// <summary>
    /// Gets or sets the current friend IDs tracked by the metadata hub connection.
    /// </summary>
    public int[] FriendIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the metadata hub stale connection disconnect callback.
    /// </summary>
    public Func<string, Task>? DisconnectMetadataHubConnection { get; set; }

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

    /// <summary>
    /// Gets or sets the spectator hub stale connection disconnect callback.
    /// </summary>
    public Func<string, Task>? DisconnectSpectatorHubConnection { get; set; }

    #endregion

    #region MultiplayerHub

    /// <summary>
    /// Gets or sets the multiplayer hub stale connection disconnect callback.
    /// </summary>
    public Func<string, Task>? DisconnectMultiplayerHubConnection { get; set; }

    #endregion

    /// <inheritdoc />
    public override Task OnPlayerOnline(IPlayer player)
    {
        return OnPlayerOnlineForMetadataHub != null ? OnPlayerOnlineForMetadataHub(player) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task OnPlayerOffline(IPlayer player, bool isKicked = false)
    {
        if (isKicked && ReferenceEquals(player, this))
        {
            await DisconnectCurrentHubConnections();
        }

        if (OnPlayerOfflineForMetadataHub != null)
        {
            await OnPlayerOfflineForMetadataHub(player, isKicked);
        }
    }

    /// <inheritdoc />
    public override Task OnPlayerChangeActivity(IPlayer player, IUserActivity activity)
    {
        return OnPlayerChangeActivityForMetadataHub != null ? OnPlayerChangeActivityForMetadataHub(player, activity) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnPlayerChangeStatus(IPlayer player, UserStatus? status)
    {
        return OnPlayerChangeStatusForMetadataHub != null ? OnPlayerChangeStatusForMetadataHub(player, status) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnUserBeganPlaying(IPlayer player)
    {
        return OnUserBeganPlayingForSpectatorHub != null ? OnUserBeganPlayingForSpectatorHub(player) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnUserFinishedPlaying(IPlayer player)
    {
        return OnUserFinishedPlayingForSpectatorHub != null ? OnUserFinishedPlayingForSpectatorHub(player) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnUserSentFrames(IPlayer player, FrameDataBundle frame)
    {
        return OnUserSentFramesForSpectatorHub != null ? OnUserSentFramesForSpectatorHub(player, frame) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnWatched(IPlayer source)
    {
        return OnWatchedForSpectatorHub != null ? OnWatchedForSpectatorHub(source) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnWatchedStopped(IPlayer source)
    {
        return OnWatchedStoppedForSpectatorHub != null ? OnWatchedStoppedForSpectatorHub(source) : Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task OnScoreProcessed(long scoreId)
    {
        return OnScoreProcessedForSpectatorHub != null ? OnScoreProcessedForSpectatorHub(scoreId) : Task.CompletedTask;
    }

    /// <summary>
    /// Marks a specific hub as connected for this player.
    /// </summary>
    /// <param name="hubName">The connected hub name.</param>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HubConnected(string hubName, string connectionId)
    {
        if (Facade.Logger != null)
        {
            Facade.Logger.LogInformation(
                "{Hub} connected for player {UserId} (Lazer).",
                hubName,
                PlayerId);
        }

        string replacedConnectionId;
        Func<string, Task>? disconnectReplacedConnection;
        bool shouldGoOnline;
        lock (_hubConnectionLock)
        {
            shouldGoOnline = !HasAnyHubConnection();

            switch (hubName)
            {
                case "MetadataHub":
                    replacedConnectionId = _metadataConnectionId;
                    disconnectReplacedConnection = DisconnectMetadataHubConnection;
                    _metadataConnectionId = connectionId;
                    break;
                case "SpectatorHub":
                    replacedConnectionId = _spectatorConnectionId;
                    disconnectReplacedConnection = DisconnectSpectatorHubConnection;
                    _spectatorConnectionId = connectionId;
                    break;
                case "MultiplayerHub":
                    replacedConnectionId = _multiplayerConnectionId;
                    disconnectReplacedConnection = DisconnectMultiplayerHubConnection;
                    _multiplayerConnectionId = connectionId;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(hubName), hubName, "Unknown hub name.");
            }
        }

        if (!string.IsNullOrEmpty(replacedConnectionId) &&
            !string.Equals(replacedConnectionId, connectionId, StringComparison.Ordinal) &&
            disconnectReplacedConnection != null)
        {
            await DisconnectReplacedHubConnection(hubName, replacedConnectionId, disconnectReplacedConnection);
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
    /// <param name="connectionId">The disconnected connection ID.</param>
    /// <param name="exception">The exception that caused the disconnection, if any.</param>
    /// <returns>Whether the disconnected connection was the current connection for the hub.</returns>
    public async Task<bool> HubDisconnected(string hubName, string connectionId, Exception? exception)
    {
        if (Facade.Logger != null)
        {
            if (exception == null)
            {
                Facade.Logger.LogInformation(
                    "{Hub} disconnected for player {UserId} (Lazer).",
                    hubName,
                    PlayerId);
            }
            else
            {
                Facade.Logger.LogWarning(
                    exception,
                    "{Hub} disconnected with an error for player {UserId} (Lazer).",
                    hubName,
                    PlayerId);
            }
        }

        bool disconnectedCurrentConnection;
        bool shouldGoOffline = false;
        lock (_hubConnectionLock)
        {
            disconnectedCurrentConnection = IsCurrentHubConnection(hubName, connectionId);
            if (disconnectedCurrentConnection)
            {
                ClearHubConnection(hubName);
                shouldGoOffline = !HasAnyHubConnection();
            }
        }

        if (!disconnectedCurrentConnection)
        {
            return false;
        }

        if (shouldGoOffline)
        {
            await Offline(isKicked: false);
        }

        return true;
    }

    private bool HasAnyHubConnection() =>
        !string.IsNullOrEmpty(_metadataConnectionId) ||
        !string.IsNullOrEmpty(_spectatorConnectionId) ||
        !string.IsNullOrEmpty(_multiplayerConnectionId);

    private void ClearHubConnection(string hubName)
    {
        switch (hubName)
        {
            case "MetadataHub":
                _metadataConnectionId = string.Empty;
                DisconnectMetadataHubConnection = null;
                OnPlayerOnlineForMetadataHub = null;
                OnPlayerOfflineForMetadataHub = null;
                OnPlayerChangeActivityForMetadataHub = null;
                OnPlayerChangeStatusForMetadataHub = null;
                break;
            case "SpectatorHub":
                _spectatorConnectionId = string.Empty;
                DisconnectSpectatorHubConnection = null;
                OnUserBeganPlayingForSpectatorHub = null;
                OnUserFinishedPlayingForSpectatorHub = null;
                OnUserSentFramesForSpectatorHub = null;
                OnWatchedForSpectatorHub = null;
                OnWatchedStoppedForSpectatorHub = null;
                OnScoreProcessedForSpectatorHub = null;
                break;
            case "MultiplayerHub":
                _multiplayerConnectionId = string.Empty;
                DisconnectMultiplayerHubConnection = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(hubName), hubName, "Unknown hub name.");
        }
    }

    private async Task DisconnectCurrentHubConnections()
    {
        List<(string HubName, string ConnectionId, Func<string, Task> DisconnectConnection)> hubConnections = [];
        lock (_hubConnectionLock)
        {
            if (!string.IsNullOrEmpty(_metadataConnectionId) && DisconnectMetadataHubConnection != null)
            {
                hubConnections.Add(("MetadataHub", _metadataConnectionId, DisconnectMetadataHubConnection));
            }

            if (!string.IsNullOrEmpty(_spectatorConnectionId) && DisconnectSpectatorHubConnection != null)
            {
                hubConnections.Add(("SpectatorHub", _spectatorConnectionId, DisconnectSpectatorHubConnection));
            }

            if (!string.IsNullOrEmpty(_multiplayerConnectionId) && DisconnectMultiplayerHubConnection != null)
            {
                hubConnections.Add(("MultiplayerHub", _multiplayerConnectionId, DisconnectMultiplayerHubConnection));
            }
        }

        foreach ((string hubName, string connectionId, Func<string, Task> disconnectConnection) in hubConnections)
        {
            await DisconnectReplacedHubConnection(hubName, connectionId, disconnectConnection);
        }
    }

    private async Task DisconnectReplacedHubConnection(
        string hubName,
        string connectionId,
        Func<string, Task> disconnectConnection)
    {
        try
        {
            await disconnectConnection(connectionId);
        }
        catch (Exception exception)
        {
            Facade.Logger?.LogWarning(
                exception,
                "Failed to request stale {Hub} connection {ConnectionId} to disconnect for player {UserId} (Lazer).",
                hubName,
                connectionId,
                PlayerId);
        }
    }

    private bool IsCurrentHubConnection(string hubName, string connectionId)
    {
        return hubName switch
        {
            "MetadataHub" => string.Equals(_metadataConnectionId, connectionId, StringComparison.Ordinal),
            "SpectatorHub" => string.Equals(_spectatorConnectionId, connectionId, StringComparison.Ordinal),
            "MultiplayerHub" => string.Equals(_multiplayerConnectionId, connectionId, StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(hubName), hubName, "Unknown hub name."),
        };
    }
}