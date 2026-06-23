// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Online.Spectator;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Manager;

/// <summary>
/// Tracks connected players and broadcasts their state changes.
/// </summary>
public class PlayerManager(ILogger<PlayerManager> logger, InterProcessCommunicationClient ipcClient)
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

            if (_pendingPlayers.TryGetValue(identity, out IPlayer? pendingPlayer) && ReferenceEquals(pendingPlayer, player))
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

    #endregion

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

    private static (int PlayerId, string PlayerServer) PlayerIdentity(IPlayer player) =>
        (player.PlayerId, player.Server);
}