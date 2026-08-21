// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Spectator;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Manager;

/// <summary>
/// Tracks connected players and broadcasts their state changes.
/// </summary>
public class PlayerManager(
    ILogger<PlayerManager> logger,
    InterProcessCommunicationClient ipcClient)
{
    private static readonly Action<ILogger, int, string, Exception?> LogPlayerConnected =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(1, nameof(AddPlayer)),
            "Player {UserId} ({Server}) connected to server.");

    private static readonly Action<ILogger, int, string, bool, Exception?> LogPlayerDisconnected =
        LoggerMessage.Define<int, string, bool>(
            LogLevel.Information,
            new EventId(2, nameof(RemovePlayer)),
            "Player {UserId} ({Server}) disconnected from server. (isKicked: {IsKicked})");

    private readonly object _lock = new();

    private readonly Dictionary<(int, string), IPlayer> _players = new();

    private readonly Dictionary<(int, string), IPlayer> _pendingPlayers = new();

    // source (watcher) -> target (watched)
    private readonly Dictionary<IPlayer, HashSet<IPlayer>> _playerWatching = new();

    // user id -> disconnect grace timer (multiplayer room membership).
    private readonly Dictionary<int, CancellationTokenSource> _disconnectGraceTimers = new();

    /// <summary>
    /// Gets or sets the grace period during which a disconnected user is kept in
    /// their room before being removed (defaults to 30 seconds; tests may shrink it).
    /// </summary>
    public TimeSpan MultiplayerDisconnectGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    #region Multiplayer disconnect grace

    /// <summary>
    /// Starts a grace timer which removes the given user from their room after
    /// the specified period unless cancelled by a reconnect. Used to keep users
    /// in a room across brief connection drops.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="roomId">The room ID the user is currently in.</param>
    /// <param name="gracePeriod">The grace period.</param>
    /// <param name="roomManager">The room manager used to resolve the room.</param>
    public void StartMultiplayerDisconnectGrace(int userId, long roomId, TimeSpan gracePeriod, RoomManager roomManager)
    {
        CancelMultiplayerDisconnectGrace(userId);

        var cancellationSource = new CancellationTokenSource();
        _disconnectGraceTimers[userId] = cancellationSource;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(gracePeriod, cancellationSource.Token).ConfigureAwait(false);

                var room = roomManager.GetRoom(roomId);
                if (room == null)
                {
                    return;
                }

                await room.RoomLock.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
                try
                {
                    await room.LeaveRoom(userId);
                }
                finally
                {
                    room.RoomLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Reconnected before the grace period elapsed.
            }
            finally
            {
                lock (_lock)
                {
                    if (_disconnectGraceTimers.TryGetValue(userId, out var current)
                        && ReferenceEquals(current, cancellationSource))
                    {
                        _disconnectGraceTimers.Remove(userId);
                    }
                }
            }
        });
    }

    /// <summary>
    /// Cancels any pending multiplayer disconnect grace timer for the user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    public void CancelMultiplayerDisconnectGrace(int userId)
    {
        lock (_lock)
        {
            if (_disconnectGraceTimers.TryGetValue(userId, out var cancellationSource))
            {
                _disconnectGraceTimers.Remove(userId);
                cancellationSource.Cancel();
            }
        }
    }

    #endregion

    #region Player Storage

    /// <summary>
    /// Adds a player to the active connection registry.
    /// </summary>
    /// <param name="player">The player to add.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddPlayer(IPlayer player)
    {
        IPlayer? existingPlayer;
        lock (_lock)
        {
            var identity = PlayerIdentity(player);
            _pendingPlayers.Remove(identity);

            if (_players.TryGetValue(identity, out existingPlayer))
            {
                if (!ReferenceEquals(existingPlayer, player))
                {
                    _players[identity] = player;
                }
                else
                {
                    existingPlayer = null;
                }
            }
            else
            {
                _players.Add(identity, player);
            }
        }

        if (existingPlayer != null)
        {
            await existingPlayer.Offline(isKicked: true);
        }

        await BroadcastPlayerOnline(player);

        LogPlayerConnected(logger, player.PlayerId, player.Server, null);
    }

    /// <summary>
    /// Removes a player from the active connection registry.
    /// </summary>
    /// <param name="player">The player to remove.</param>
    /// <param name="isKicked">Whether the player was removed because another connection replaced them.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemovePlayer(IPlayer player, bool isKicked = false)
    {
        List<IPlayer> watchingStopped;
        lock (_lock)
        {
            var identity = PlayerIdentity(player);
            if (_players.TryGetValue(identity, out IPlayer? currentPlayer) && ReferenceEquals(currentPlayer, player))
            {
                _players.Remove(identity);
            }

            if (_pendingPlayers.TryGetValue(identity, out IPlayer? pendingPlayer) &&
                ReferenceEquals(pendingPlayer, player))
            {
                _pendingPlayers.Remove(identity);
            }

            _playerWatching.Remove(player);
            watchingStopped = _playerWatching
                .Where(kv => kv.Value.Remove(player))
                .Select(kv => kv.Key)
                .ToList();
        }

        foreach (var watcher in watchingStopped)
        {
            await watcher.OnWatchedStopped(player);
        }

        await BroadcastPlayerOffline(player, isKicked);

        LogPlayerDisconnected(logger, player.PlayerId, player.Server, isKicked, null);
    }

    /// <summary>
    /// Gets an existing player or creates one atomically for concurrent hub connections.
    /// </summary>
    /// <typeparam name="T">The expected player type.</typeparam>
    /// <param name="playerId">The player ID.</param>
    /// <param name="server">The server identifier.</param>
    /// <param name="playerFactory">The factory used when a player does not already exist.</param>
    /// <param name="updatePlayer">The callback used to update an existing player instance.</param>
    /// <returns>The existing or newly created player.</returns>
    public T GetOrCreatePlayer<T>(int playerId, string server, Func<T> playerFactory, Action<T> updatePlayer)
        where T : class, IPlayer
    {
        lock (_lock)
        {
            var identity = (playerId, server);
            if (_players.TryGetValue(identity, out IPlayer? activePlayer))
            {
                var player = (T)activePlayer;
                updatePlayer(player);
                return player;
            }

            if (_pendingPlayers.TryGetValue(identity, out IPlayer? pendingPlayer))
            {
                var player = (T)pendingPlayer;
                updatePlayer(player);
                return player;
            }

            var createdPlayer = playerFactory();
            _pendingPlayers.Add(PlayerIdentity(createdPlayer), createdPlayer);
            return createdPlayer;
        }
    }

    /// <summary>
    /// Gets the player instance for a specific server connection.
    /// </summary>
    /// <typeparam name="T">The expected player type.</typeparam>
    /// <param name="playerId">The player ID.</param>
    /// <param name="server">The server identifier.</param>
    /// <returns>The matching player if present; otherwise, <see langword="null"/>.</returns>
    public T? GetPlayer<T>(int playerId, string server)
        where T : class, IPlayer
    {
        lock (_lock)
        {
            _players.TryGetValue((playerId, server), out var player);
            return (T?)player;
        }
    }

    /// <summary>
    /// Gets the first connected instance of a player matching the supplied type.
    /// </summary>
    /// <typeparam name="T">The expected player type.</typeparam>
    /// <param name="playerId">The player ID.</param>
    /// <returns>The first matching player if present; otherwise, <see langword="null"/>.</returns>
    public T? GetPlayer<T>(int playerId)
        where T : class, IPlayer
    {
        IPlayer[] players;
        lock (_lock)
        {
            players = _players.Values.Where(p => p.PlayerId == playerId).ToArray();
        }

        foreach (var player in players)
        {
            if (player is T specifiedPlayer)
            {
                return specifiedPlayer;
            }
        }

        return default;
    }

    /// <summary>
    /// Gets every connected instance for the specified player ID.
    /// </summary>
    /// <param name="playerId">The player ID.</param>
    /// <returns>All connected instances for the player.</returns>
    public IEnumerable<IPlayer> GetPlayerAllInstances(int playerId)
    {
        IPlayer[] players;
        lock (_lock)
        {
            players = _players.Values.Where(p => p.PlayerId == playerId).ToArray();
        }

        foreach (var player in players)
        {
            yield return player;
        }
    }

    /// <summary>
    /// Gets all connected players.
    /// </summary>
    /// <returns>All connected players.</returns>
    public IEnumerable<IPlayer> GetAllPlayers()
    {
        lock (_lock)
        {
            return _players.Values.ToArray();
        }
    }

    /// <summary>
    /// Gets all connected players of a specific type.
    /// </summary>
    /// <typeparam name="T">The expected player type.</typeparam>
    /// <returns>All connected players of the requested type.</returns>
    public IEnumerable<T> GetAllPlayers<T>()
        where T : class, IPlayer
    {
        lock (_lock)
        {
            return _players.Values.OfType<T>().ToArray();
        }
    }

    #endregion

    #region Online Status

    /// <summary>
    /// Broadcasts that a player came online.
    /// </summary>
    /// <param name="player">The player that came online.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BroadcastPlayerOnline(IPlayer player)
    {
        await ipcClient.SendNoticeAsync("lazer", "user_online_status_changed", new { user_id = player.PlayerId });

        // Follow osu!'s hiding online status policy.
        if (player.State.UserStatus == UserStatus.Offline)
        {
            return;
        }

        await Task.WhenAll(
            GetAllPlayers()
                .Select(p => p.OnPlayerOnline(player)));
    }

    /// <summary>
    /// Broadcasts that a player went offline.
    /// </summary>
    /// <param name="player">The player that went offline.</param>
    /// <param name="isKicked">Whether the player was kicked because of a replacement connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BroadcastPlayerOffline(IPlayer player, bool isKicked = false)
    {
        await ipcClient.SendNoticeAsync("lazer", "user_online_status_changed", new { user_id = player.PlayerId });

        // Follow osu!'s hiding online status policy.
        if (player.State.UserStatus == UserStatus.Offline)
        {
            return;
        }

        await Task.WhenAll(
            GetAllPlayers()
                .Select(p => p.OnPlayerOffline(player, isKicked)));
    }

    /// <summary>
    /// Broadcasts that a player changed activity.
    /// </summary>
    /// <param name="player">The player that changed activity.</param>
    /// <param name="activity">The updated activity.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BroadcastPlayerChangeActivity(IPlayer player, IUserActivity activity)
    {
        // Follow osu!'s hiding online status policy.
        if (player.State.UserStatus == UserStatus.Offline)
        {
            return;
        }

        await Task.WhenAll(
            GetAllPlayers()
                .Select(p => p.OnPlayerChangeActivity(player, activity)));
    }

    /// <summary>
    /// Broadcasts that a player changed status.
    /// </summary>
    /// <param name="player">The player that changed status.</param>
    /// <param name="status">The updated status.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BroadcastPlayerChangeStatus(IPlayer player, UserStatus? status)
    {
        await Task.WhenAll(
            GetAllPlayers()
                .Select(p => p.OnPlayerChangeStatus(player, status)));
    }

    #endregion

    #region Spectator

    /// <summary>
    /// Broadcasts that a watched player began a spectator play session.
    /// </summary>
    /// <param name="player">The player that began playing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BroadcastUserBeganPlaying(IPlayer player)
    {
        var watchers = GetWatchingPlayers(player).ToArray();
        logger.LogInformation(
            "Broadcasting spectator begin-play for player {UserId} ({Server}) to {WatcherCount} watcher(s). ScoreToken: {ScoreToken}.",
            player.PlayerId,
            player.Server,
            watchers.Length,
            player.State.ScoreToken);

        await Task.WhenAll(
            watchers
                .Select(p => p.OnUserBeganPlaying(player)));
    }

    /// <summary>
    /// Broadcasts that a watched player finished a spectator play session.
    /// </summary>
    /// <param name="player">The player that finished playing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BroadcastUserFinishedPlaying(IPlayer player)
    {
        var watchers = GetWatchingPlayers(player).ToArray();
        logger.LogInformation(
            "Broadcasting spectator finish-play for player {UserId} ({Server}) to {WatcherCount} watcher(s). ScoreToken: {ScoreToken}.",
            player.PlayerId,
            player.Server,
            watchers.Length,
            player.State.ScoreToken);

        await Task.WhenAll(
            watchers
                .Select(p => p.OnUserFinishedPlaying(player)));
    }

    /// <summary>
    /// Broadcasts spectator frame data to players watching the source player.
    /// </summary>
    /// <param name="player">The player that sent frames.</param>
    /// <param name="frame">The frame bundle to broadcast.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BroadcastUserSentFrames(IPlayer player, FrameDataBundle frame)
    {
        var watchers = GetWatchingPlayers(player).ToArray();
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Broadcasting spectator frame data for player {UserId} ({Server}) to {WatcherCount} watcher(s). ScoreToken: {ScoreToken}, FrameCount: {FrameCount}.",
                player.PlayerId,
                player.Server,
                watchers.Length,
                player.State.ScoreToken,
                frame.Frames.Count);
        }

        await Task.WhenAll(
            watchers
                .Select(p => p.OnUserSentFrames(player, frame)));
    }

    /// <summary>
    /// Registers that one player is watching another player.
    /// </summary>
    /// <param name="source">The watching player.</param>
    /// <param name="target">The watched player.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UserWatchingPlayer(IPlayer source, IPlayer target)
    {
        bool added;
        lock (_lock)
        {
            if (!_playerWatching.TryGetValue(source, out HashSet<IPlayer>? value))
            {
                var players = new HashSet<IPlayer> { target };
                _playerWatching.Add(source, players);
                added = true;
            }
            else
            {
                added = value.Add(target);
            }
        }

        logger.LogInformation(
            "Player {WatcherUserId} ({WatcherServer}) is watching player {TargetUserId} ({TargetServer}). AlreadyWatching: {AlreadyWatching}.",
            source.PlayerId,
            source.Server,
            target.PlayerId,
            target.Server,
            !added);

        if (added)
        {
            await target.OnWatched(source);
        }

        if (target.State.SpectatorState != null && target.State.SpectatorState.IsPlaying())
        {
            logger.LogDebug(
                "Replaying current spectator begin-play state from player {TargetUserId} ({TargetServer}) to watcher {WatcherUserId} ({WatcherServer}). ScoreToken: {ScoreToken}.",
                target.PlayerId,
                target.Server,
                source.PlayerId,
                source.Server,
                target.State.ScoreToken);

            await source.OnUserBeganPlaying(target);
        }
    }

    /// <summary>
    /// Registers that one player stopped watching another player.
    /// </summary>
    /// <param name="source">The watching player.</param>
    /// <param name="target">The watched player.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UserStoppedWatchingPlayer(IPlayer source, IPlayer target)
    {
        bool removed;
        lock (_lock)
        {
            removed = _playerWatching.TryGetValue(source, out HashSet<IPlayer>? value) && value.Remove(target);
        }

        logger.LogInformation(
            "Player {WatcherUserId} ({WatcherServer}) stopped watching player {TargetUserId} ({TargetServer}). WasWatching: {WasWatching}.",
            source.PlayerId,
            source.Server,
            target.PlayerId,
            target.Server,
            removed);

        if (removed)
        {
            await target.OnWatchedStopped(source);
        }
    }

    /// <summary>
    /// Gets all players currently watching the specified target.
    /// </summary>
    /// <param name="target">The watched player.</param>
    /// <returns>The players watching the target.</returns>
    public IEnumerable<IPlayer> GetWatchingPlayers(IPlayer target)
    {
        IPlayer[] watchers;
        lock (_lock)
        {
            watchers = _playerWatching
                .Where(kv => kv.Value.Contains(target))
                .Select(kv => kv.Key)
                .ToArray();
        }

        return watchers;
    }

    #endregion

    #region Multiplayer

    /// <summary>
    /// Joins a player into a room, updating the player's current-room state.
    /// </summary>
    /// <param name="roomManager">The room manager.</param>
    /// <param name="player">The joining player.</param>
    /// <param name="roomId">The room ID.</param>
    /// <param name="role">The role to join with.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UserJoinRoom(
        RoomManager roomManager,
        IPlayer player,
        long roomId,
        MultiplayerRoomUserRole role = MultiplayerRoomUserRole.Player)
    {
        var room = roomManager.GetRoom(roomId);
        if (room != null)
        {
            if (player.State.CurrentRoom != null)
            {
                throw new InvalidStateException("Player is already in a room.");
            }

            var userCanJoin = await room.UserCanJoin(player.PlayerId);
            if (!userCanJoin)
            {
                throw new InvalidStateException("Player is not eligible to join this room.");
            }

            await room.JoinRoom(player.PlayerId, role);
            player.State.CurrentRoom = roomId;
        }
        else
        {
            throw new InvalidStateException("Room not found.");
        }
    }

    /// <summary>
    /// Leaves the room the player is currently in, clearing the player's current-room state.
    /// </summary>
    /// <param name="roomManager">The room manager.</param>
    /// <param name="player">The leaving player.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UserLeaveRoom(RoomManager roomManager, IPlayer player)
    {
        long? roomId = player.State.CurrentRoom;
        if (roomId == null)
        {
            return;
        }

        var room = roomManager.GetRoom(roomId.Value);
        if (room == null)
        {
            player.State.CurrentRoom = null;
            return;
        }

        await room.LeaveRoom(player.PlayerId);
        player.State.CurrentRoom = null;
    }

    #endregion

    private static (int PlayerId, string PlayerServer) PlayerIdentity(IPlayer player) =>
        (player.PlayerId, player.Server);
}