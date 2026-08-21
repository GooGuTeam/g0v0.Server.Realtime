// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using Room = g0v0.Server.Common.Database.Models.Room;

namespace g0v0.Server.Realtime.Tests.Hubs;

/// <summary>
/// In-memory <see cref="IRoomRepository"/> used by multiplayer hub tests.
/// </summary>
internal sealed class FakeRoomRepository : IRoomRepository
{
    private readonly List<Room> _rooms = new();
    private readonly List<Playlist> _playlists = new();
    private readonly List<RoomParticipatedUser> _participants = new();
    private long _nextRoomId = 1;
    private long _nextPlaylistId = 1;
    private long _nextParticipantId = 1;

    public IReadOnlyList<Room> Rooms => _rooms;

    /// <summary>
    /// Gets the playlist item IDs which are considered to have associated score tokens.
    /// </summary>
    public HashSet<long> ScoreTokenPlaylistItemIds { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="AddUserToRoom"/> should
    /// throw, simulating a persistence failure during join.
    /// </summary>
    public bool FailAddUserToRoom { get; set; }

    /// <summary>
    /// Gets the participant records of a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The participant records.</returns>
    public IReadOnlyList<RoomParticipatedUser> GetParticipants(long roomId)
        => _participants.Where(p => p.RoomId == roomId).ToList();

    public async Task<Room> CreateRoom(
        MultiplayerRoom room,
        int hostUserId,
        G0V0RoomCategory category = G0V0RoomCategory.Normal,
        bool tournamentMode = false)
    {
        // Respect an explicit room ID (used by join-or-create test flows which
        // reference a fixed room ID), otherwise allocate a fresh one.
        long roomId = room.RoomID > 0 ? room.RoomID : _nextRoomId++;

        Room dbRoom = new()
        {
            Id = roomId,
            Name = room.Settings.Name,
            HostId = hostUserId,
            Category = category,
            Status = RoomStatus.Idle,
            Type = room.Settings.MatchType,
            Password = room.Settings.Password,
            QueueMode = room.Settings.QueueMode,
            ParticipantCount = 1,
            AutoSkip = room.Settings.AutoSkip,
            AutoStartDuration = (int)room.Settings.AutoStartDuration.TotalSeconds,
            MaxParticipants = room.Settings.MaxParticipants,
            TournamentMode = tournamentMode,
            Playlists = new List<Playlist>(),
        };

        foreach (MultiplayerPlaylistItem item in room.Playlist)
        {
            dbRoom.Playlists.Add(AddPlaylistInternal(item, dbRoom.Id, hostUserId));
        }

        _rooms.Add(dbRoom);
        await Task.CompletedTask;
        return dbRoom;
    }

    public Task<Room?> GetRoom(long roomId)
    {
        Room? room = _rooms.Find(r => r.Id == roomId);
        if (room == null)
        {
            return Task.FromResult<Room?>(null);
        }

        // Include playlist.
        room.Playlists = _playlists.Where(p => p.RoomId == roomId).ToList();
        return Task.FromResult<Room?>(room);
    }

    public async Task AddUserToRoom(long roomId, int userId)
    {
        if (FailAddUserToRoom)
        {
            throw new InvalidOperationException("Simulated AddUserToRoom failure.");
        }

        Room? room = _rooms.Find(r => r.Id == roomId) ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        if (room.EndsAt != null)
        {
            throw new InvalidOperationException($"Room with ID {roomId} has ended.");
        }

        List<RoomParticipatedUser> active = _participants
            .Where(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null)
            .OrderBy(p => p.JoinedAt)
            .ToList();

        if (active.Count > 0)
        {
            foreach (RoomParticipatedUser? extra in active.Skip(1))
            {
                extra.LeftAt = DateTimeOffset.UtcNow;
            }

            active[0].JoinedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _participants.Add(new RoomParticipatedUser
            {
                Id = _nextParticipantId++,
                RoomId = roomId,
                UserId = userId,
                JoinedAt = DateTimeOffset.UtcNow,
                LeftAt = null,
            });
        }

        await UpdateParticipantCount(roomId);
    }

    public async Task RemoveUserFromRoom(long roomId, int userId)
    {
        Room? room = _rooms.Find(r => r.Id == roomId) ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        if (room.EndsAt != null)
        {
            return;
        }

        RoomParticipatedUser? participant = _participants.Find(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null);
        if (participant != null)
        {
            participant.LeftAt = DateTimeOffset.UtcNow;
        }

        await UpdateParticipantCount(roomId);
        await EndRoomIfEmpty(roomId);
    }

    public Task UpdateRoomHost(long roomId, int? hostUserId)
    {
        Room room = _rooms.Find(r => r.Id == roomId)
                   ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        room.HostId = hostUserId;
        return Task.CompletedTask;
    }

    public Task UpdateRoomSettings(long roomId, MultiplayerRoomSettings settings)
    {
        Room room = _rooms.Find(r => r.Id == roomId)
                   ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        room.Name = settings.Name;
        room.Password = settings.Password;
        room.Type = settings.MatchType;
        room.QueueMode = settings.QueueMode;
        room.AutoSkip = settings.AutoSkip;
        room.AutoStartDuration = (int)settings.AutoStartDuration.TotalSeconds;
        room.MaxParticipants = settings.MaxParticipants;
        return Task.CompletedTask;
    }

    public Task UpdateRoomStatus(long roomId, MultiplayerRoomState state)
    {
        Room room = _rooms.Find(r => r.Id == roomId)
                   ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        room.Status = state is MultiplayerRoomState.Playing or MultiplayerRoomState.WaitingForLoad
            ? RoomStatus.Playing
            : RoomStatus.Idle;
        return Task.CompletedTask;
    }

    public Task SetRoomEndDate(long roomId, DateTimeOffset? endDate)
    {
        Room room = _rooms.Find(r => r.Id == roomId)
                   ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        room.EndsAt = endDate;
        return Task.CompletedTask;
    }

    public Task<Playlist> AddItemToPlaylist(MultiplayerPlaylistItem item, long roomId, int ownerId)
    {
        Playlist playlist = AddPlaylistInternal(item, roomId, ownerId);
        return Task.FromResult(playlist);
    }

    public Task<Playlist> EditItemToPlaylist(MultiplayerPlaylistItem item, long roomId, int ownerId)
    {
        Playlist playlist = _playlists.Find(p => p.Id == item.ID && p.RoomId == roomId)
                       ?? throw new InvalidOperationException($"Playlist item with ID {item.ID} does not exist in room {roomId}.");
        playlist.BeatmapId = item.BeatmapID;
        playlist.RulesetId = item.RulesetID;
        playlist.OwnerId = item.OwnerID;
        playlist.AllowedMods = item.AllowedMods.ToList();
        playlist.RequiredMods = item.RequiredMods.ToList();
        playlist.Freestyle = item.Freestyle;
        playlist.Expired = item.Expired;
        playlist.PlaylistOrder = item.PlaylistOrder;
        playlist.PlayedAt = item.PlayedAt;
        return Task.FromResult(playlist);
    }

    public Task<Playlist> RemoveItemToPlaylist(long itemId, long roomId, int ownerId)
    {
        Playlist playlist = _playlists.Find(p => p.Id == itemId && p.RoomId == roomId)
                       ?? throw new InvalidOperationException($"Playlist item with ID {itemId} does not exist in room {roomId}.");
        _playlists.Remove(playlist);
        return Task.FromResult(playlist);
    }

    public Task<bool> AnyScoreTokenExistsFor(long playlistItemId)
        => Task.FromResult(ScoreTokenPlaylistItemIds.Contains(playlistItemId));

    public Task DeleteRoom(long roomId)
    {
        _rooms.RemoveAll(r => r.Id == roomId);
        _playlists.RemoveAll(p => p.RoomId == roomId);
        _participants.RemoveAll(p => p.RoomId == roomId);
        return Task.CompletedTask;
    }

    private Playlist AddPlaylistInternal(MultiplayerPlaylistItem item, long roomId, int ownerId)
    {
        Playlist playlist = new()
        {
            Id = _nextPlaylistId++,
            RoomId = roomId,
            BeatmapId = item.BeatmapID,
            RulesetId = item.RulesetID,
            AllowedMods = item.AllowedMods.ToList(),
            RequiredMods = item.RequiredMods.ToList(),
            Freestyle = item.Freestyle,
            Expired = item.Expired,
            OwnerId = ownerId,
            PlaylistOrder = item.PlaylistOrder,
            WinCondition = item.WinCondition,
        };
        _playlists.Add(playlist);
        return playlist;
    }

    private async Task UpdateParticipantCount(long roomId)
    {
        Room? room = _rooms.Find(r => r.Id == roomId);
        if (room != null)
        {
            room.ParticipantCount = _participants.Count(p => p.RoomId == roomId && p.LeftAt == null);
        }

        await Task.CompletedTask;
    }

    private async Task EndRoomIfEmpty(long roomId)
    {
        Room? room = _rooms.Find(r => r.Id == roomId);
        if (room == null)
        {
            return;
        }

        if (_participants.Exists(p => p.RoomId == roomId && p.LeftAt == null))
        {
            return;
        }

        if (room.EndsAt == null)
        {
            room.EndsAt = DateTimeOffset.UtcNow;
            room.ParticipantCount = 0;
        }

        await Task.CompletedTask;
    }
}