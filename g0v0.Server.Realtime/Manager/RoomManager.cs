// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Objects.Rooms;
using osu.Game.Online.Multiplayer;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Manager;

/// <summary>
/// Tracks active multiplayer rooms and provides thread-safe access to them.
/// </summary>
public class RoomManager(
    ILogger<RoomManager> logger,
    IServiceScopeFactory scopeFactory,
    IRoomFacade roomFacade)
{
    private readonly ConcurrentDictionary<long, IRoom> _rooms = new();

    /// <summary>
    /// Gets the room with the given ID.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The room, or <see langword="null"/> if not found.</returns>
    public IRoom? GetRoom(long roomId)
    {
        return _rooms.GetValueOrDefault(roomId);
    }

    /// <summary>
    /// Registers a room in the manager and wires its disband callback.
    /// </summary>
    /// <param name="room">The room to register.</param>
    public void AddRoom(IRoom room)
    {
        if (room is RoomBase roomBase)
        {
            roomBase.OnDisbanded = () =>
            {
                RemoveRoom(roomBase.RoomId);
                return Task.CompletedTask;
            };

            roomBase.OnMatchTypeChanged = newType => ChangeMatchType(roomBase.RoomId, newType);
        }

        _rooms[room.RoomId] = room;
    }

    /// <summary>
    /// Removes a room from the manager.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    public void RemoveRoom(long roomId)
    {
        // Disbanding is handled by room itself.
        _rooms.TryRemove(roomId, out _);
    }

    /// <summary>
    /// Creates a new multiplayer room of the given match type and registers it.
    /// </summary>
    /// <param name="room">The room details from the client.</param>
    /// <param name="hostUserId">The user ID of the room host.</param>
    /// <param name="tournamentMode">Whether the room is a tournament room.</param>
    /// <returns>The created room.</returns>
    /// <exception cref="InvalidStateException">The match type is not supported.</exception>
    public async Task<IRoom> CreateRoom(
        MultiplayerRoom room,
        int hostUserId,
        bool tournamentMode = false)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IRoomRepository roomRepository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();

        // Validate the playlist items' mods before persisting the room.
        if (roomFacade.RulesetManager != null)
        {
            foreach (osu.Game.Online.Rooms.MultiplayerPlaylistItem item in room.Playlist)
            {
                if (item.Freestyle && item.AllowedMods.Any())
                {
                    throw new InvalidStateException("Cannot enqueue freestyle item with mods.");
                }

                item.EnsureModsValid(roomFacade.RulesetManager);
            }
        }

        Room dbRoom = await roomRepository.CreateRoom(room, hostUserId, G0V0RoomCategory.Realtime, tournamentMode);

        IRoom roomInstance;
        try
        {
            roomInstance = CreateRoomInstance(dbRoom, room.Settings.MatchType);

            // Initialise the match-type specific state.
            await ((RoomBase)roomInstance).InitializeMatchState();
        }
        catch
        {
            // Clean up the orphaned database room so failed creations do not leak.
            await roomRepository.DeleteRoom(dbRoom.Id);
            throw;
        }

        roomInstance.ChannelId = await EnsureRoomChannelAsync(dbRoom);

        AddRoom(roomInstance);
        logger.LogInformation(
            "Created room {RoomId} of type {MatchType} hosted by {HostUserId}.",
            roomInstance.RoomId,
            room.Settings.MatchType,
            hostUserId);

        return roomInstance;
    }

    /// <summary>
    /// Retrieves the room with the given ID, or loads it from the database when
    /// it is not currently active.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The room, or <see langword="null"/> if it does not exist.</returns>
    public async Task<IRoom?> GetOrLoadRoom(long roomId)
    {
        if (_rooms.TryGetValue(roomId, out IRoom? existing))
        {
            return existing;
        }

        using IServiceScope scope = scopeFactory.CreateScope();
        IRoomRepository roomRepository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        Room? dbRoom = await roomRepository.GetRoom(roomId);

        if (dbRoom == null)
        {
            return null;
        }

        // Rooms whose end date has passed are considered destroyed and are not
        // reloaded. A future end date (e.g. an empty tournament room awaiting
        // destruction) is loaded and the end date is cleared so it can be joined
        // again.
        if (dbRoom.EndsAt != null && dbRoom.EndsAt.Value.UtcDateTime < DateTime.UtcNow)
        {
            return null;
        }

        if (dbRoom.EndsAt != null)
        {
            await roomRepository.SetRoomEndDate(roomId, endDate: null);
        }

        IRoom roomInstance = CreateRoomInstance(dbRoom, dbRoom.Type);

        await ((RoomBase)roomInstance).InitializeMatchState();

        if (roomInstance.ChannelId == 0)
        {
            roomInstance.ChannelId = await EnsureRoomChannelAsync(dbRoom);
        }

        AddRoom(roomInstance);
        logger.LogInformation("Loaded room {RoomId} from database.", roomInstance.RoomId);

        return roomInstance;
    }

    /// <summary>
    /// Changes the match type of an existing room, swapping its implementation
    /// instance while keeping the same room ID and settings. The database record
    /// is updated in place (only the type field changes); the old instance is
    /// destroyed and replaced by a new one of the target type.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="newType">The new match type.</param>
    /// <returns>The new room instance.</returns>
    /// <exception cref="InvalidStateException">The room does not exist or the match type is unsupported.</exception>
    public async Task<IRoom> ChangeMatchType(long roomId, OsuMatchType newType)
    {
        if (!_rooms.TryGetValue(roomId, out IRoom? existing))
        {
            throw new InvalidStateException("Room not found.");
        }

        if (newType is not (OsuMatchType.HeadToHead or OsuMatchType.TeamVersus))
        {
            throw new InvalidStateException("Invalid match type selected.");
        }

        RoomBase oldRoom = (RoomBase)existing;

        // The lock may already be held by the caller (e.g. when invoked through
        // OnMatchTypeChanged from ChangeRoomSettings, which runs inside the room
        // lock). SemaphoreSlim is not re-entrant, so only acquire it when free.
        bool lockTaken = await oldRoom.RoomLock.WaitAsync(0);
        try
        {
            // Update the database record in place (type field only); the record
            // itself is not deleted and recreated.
            MultiplayerRoomSettings updatedSettings = oldRoom.Settings;
            updatedSettings.MatchType = newType;
            await oldRoom.UpdateRoomSettings(updatedSettings);

            // Build the new instance from the same database room (same id/settings).
            using IServiceScope scope = scopeFactory.CreateScope();
            IRoomRepository roomRepository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            Room dbRoom = await roomRepository.GetRoom(roomId)
                          ?? throw new InvalidStateException("Room not found.");

            IRoom newInstance = CreateRoomInstance(dbRoom, newType);

            // Migrate live in-memory state (users, host, playlist, bans) and
            // initialise the new match-type specific state.
            await ((RoomBase)newInstance).CopyStateFrom(oldRoom);

            // Swap the registry entry and wire callbacks on the replacement.
            oldRoom.OnDisbanded = null;
            oldRoom.OnMatchTypeChanged = null;
            AddRoom(newInstance);

            logger.LogInformation("Room {RoomId} changed match type to {MatchType}.", roomId, newType);
            return newInstance;
        }
        finally
        {
            if (lockTaken)
            {
                oldRoom.RoomLock.Release();
            }
        }
    }

    /// <summary>
    /// Gets all currently active rooms.
    /// </summary>
    /// <returns>All currently active rooms.</returns>
    public IEnumerable<IRoom> GetAllRooms() => _rooms.Values.ToArray();

    /// <summary>
    /// Creates a room instance of the given match type.
    /// </summary>
    /// <param name="dbRoom">The database room.</param>
    /// <param name="matchType">The match type.</param>
    /// <returns>The room instance.</returns>
    /// <exception cref="InvalidStateException">The match type is not supported.</exception>
    private IRoom CreateRoomInstance(Room dbRoom, OsuMatchType matchType)
    {
        return matchType switch
        {
            OsuMatchType.TeamVersus => new TeamVersusRoom(dbRoom, roomFacade),
            OsuMatchType.HeadToHead => new HeadToHeadRoom(dbRoom, roomFacade),
            _ => throw new InvalidStateException($"Unsupported match type '{matchType}'."),
        };
    }

    /// <summary>
    /// Ensures a multiplayer chat channel exists for a room, creating it with
    /// the name <c>mp_{roomId}</c> when needed and returning its channel ID.
    /// </summary>
    /// <param name="dbRoom">The database room.</param>
    /// <returns>The chat channel ID.</returns>
    private async Task<int> EnsureRoomChannelAsync(Room dbRoom)
    {
        if (dbRoom.ChannelId > 0)
        {
            return dbRoom.ChannelId;
        }

        using IServiceScope scope = scopeFactory.CreateScope();
        IChatRepository chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();

        ChatChannel channel = await chatRepository.GetOrCreateMultiplayerChannelAsync(dbRoom.Id);
        if (dbRoom.ChannelId == channel.ChannelId)
        {
            return channel.ChannelId;
        }

        IRoomRepository roomRepository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        await roomRepository.UpdateRoomChannelId(dbRoom.Id, channel.ChannelId);
        return channel.ChannelId;
    }
}