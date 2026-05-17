// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Users;
using StackExchange.Redis;

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

    private static readonly Action<ILogger, int, string, Exception?> LogPlayerDisconnected =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(2, nameof(RemovePlayer)),
            "Player {UserId} ({Server}) disconnected from server.");

    private readonly Dictionary<(int, string), IPlayer> _players = new();

    #region Player Storage

    /// <summary>
    /// Adds a player to the active connection registry.
    /// </summary>
    /// <param name="player">The player to add.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddPlayer(IPlayer player)
    {
        // if player from the same server connected before, kick the previous connection.
        if (_players.TryGetValue(PlayerIdentity(player), out var existingPlayer))
        {
            await existingPlayer.Offline(isKicked: true);
        }

        _players.Add(PlayerIdentity(player), player);
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
        _players.Remove(PlayerIdentity(player));
        await BroadcastPlayerOffline(player, isKicked);

        LogPlayerDisconnected(logger, player.PlayerId, player.Server, null);
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
        _players.TryGetValue((playerId, server), out var player);
        return (T?)player;
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
        var players = _players.Values.Where(p => p.PlayerId == playerId).ToArray();
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
        var players = _players.Values.Where(p => p.PlayerId == playerId).ToArray();
        foreach (var player in players)
        {
            yield return player;
        }
    }

    /// <summary>
    /// Gets all connected players.
    /// </summary>
    /// <returns>All connected players.</returns>
    public IEnumerable<IPlayer> GetAllPlayers() => _players.Values;

    /// <summary>
    /// Gets all connected players of a specific type.
    /// </summary>
    /// <typeparam name="T">The expected player type.</typeparam>
    /// <returns>All connected players of the requested type.</returns>
    public IEnumerable<T> GetAllPlayers<T>()
        where T : class, IPlayer => _players.Values.OfType<T>();

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

    private static (int PlayerId, string PlayerServer) PlayerIdentity(IPlayer player) =>
        (player.PlayerId, player.Server);

    #endregion
}