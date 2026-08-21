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
    private readonly List<Room> rooms = new();
    private readonly List<Playlist> playlists = new();
    private readonly List<RoomParticipatedUser> participants = new();
    private long nextRoomId = 1;
    private long nextPlaylistId = 1;
    private long nextParticipantId = 1;

    public IReadOnlyList<Room> Rooms => rooms;

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
        => participants.Where(p => p.RoomId == roomId).ToList();

    public async Task<Room> CreateRoom(
        MultiplayerRoom room,
        int hostUserId,
        G0V0RoomCategory category = G0V0RoomCategory.Normal,
        bool tournamentMode = false)
    {
        // Respect an explicit room ID (used by join-or-create test flows which
        // reference a fixed room ID), otherwise allocate a fresh one.
        long roomId = room.RoomID > 0 ? room.RoomID : nextRoomId++;

        var dbRoom = new Room
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

        foreach (var item in room.Playlist)
        {
            dbRoom.Playlists.Add(AddPlaylistInternal(item, dbRoom.Id, hostUserId));
        }

        rooms.Add(dbRoom);
        await Task.CompletedTask;
        return dbRoom;
    }

    public Task<Room?> GetRoom(long roomId)
    {
        var room = rooms.FirstOrDefault(r => r.Id == roomId);
        if (room == null)
        {
            return Task.FromResult<Room?>(null);
        }

        // Include playlist.
        room.Playlists = playlists.Where(p => p.RoomId == roomId).ToList();
        return Task.FromResult<Room?>(room);
    }

    public async Task AddUserToRoom(long roomId, int userId)
    {
        if (FailAddUserToRoom)
        {
            throw new InvalidOperationException("Simulated AddUserToRoom failure.");
        }

        var room = rooms.FirstOrDefault(r => r.Id == roomId);
        if (room == null)
        {
            throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        }

        if (room.EndsAt != null)
        {
            throw new InvalidOperationException($"Room with ID {roomId} has ended.");
        }

        var active = participants
            .Where(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null)
            .OrderBy(p => p.JoinedAt)
            .ToList();

        if (active.Count > 0)
        {
            foreach (var extra in active.Skip(1))
            {
                extra.LeftAt = DateTimeOffset.UtcNow;
            }

            active[0].JoinedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            participants.Add(new RoomParticipatedUser
            {
                Id = nextParticipantId++,
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
        var room = rooms.FirstOrDefault(r => r.Id == roomId);
        if (room == null)
        {
            throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        }

        if (room.EndsAt != null)
        {
            return;
        }

        var participant = participants.FirstOrDefault(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null);
        if (participant != null)
        {
            participant.LeftAt = DateTimeOffset.UtcNow;
        }

        await UpdateParticipantCount(roomId);
        await EndRoomIfEmpty(roomId);
    }

    public Task UpdateRoomHost(long roomId, int? hostUserId)
    {
        var room = rooms.FirstOrDefault(r => r.Id == roomId)
                   ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        room.HostId = hostUserId;
        return Task.CompletedTask;
    }

    public Task UpdateRoomSettings(long roomId, MultiplayerRoomSettings settings)
    {
        var room = rooms.FirstOrDefault(r => r.Id == roomId)
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
        var room = rooms.FirstOrDefault(r => r.Id == roomId)
                   ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        room.Status = state == MultiplayerRoomState.Playing || state == MultiplayerRoomState.WaitingForLoad
            ? RoomStatus.Playing
            : RoomStatus.Idle;
        return Task.CompletedTask;
    }

    public Task SetRoomEndDate(long roomId, DateTimeOffset? endDate)
    {
        var room = rooms.FirstOrDefault(r => r.Id == roomId)
                   ?? throw new InvalidOperationException($"Room with ID {roomId} does not exist.");
        room.EndsAt = endDate;
        return Task.CompletedTask;
    }

    public Task<Playlist> AddItemToPlaylist(MultiplayerPlaylistItem item, long roomId, int ownerId)
    {
        var playlist = AddPlaylistInternal(item, roomId, ownerId);
        return Task.FromResult(playlist);
    }

    public Task<Playlist> EditItemToPlaylist(MultiplayerPlaylistItem item, long roomId, int ownerId)
    {
        var playlist = playlists.FirstOrDefault(p => p.Id == item.ID && p.RoomId == roomId)
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
        var playlist = playlists.FirstOrDefault(p => p.Id == itemId && p.RoomId == roomId)
                       ?? throw new InvalidOperationException($"Playlist item with ID {itemId} does not exist in room {roomId}.");
        playlists.Remove(playlist);
        return Task.FromResult(playlist);
    }

    public Task<bool> AnyScoreTokenExistsFor(long playlistItemId)
        => Task.FromResult(ScoreTokenPlaylistItemIds.Contains(playlistItemId));

    public Task DeleteRoom(long roomId)
    {
        rooms.RemoveAll(r => r.Id == roomId);
        playlists.RemoveAll(p => p.RoomId == roomId);
        participants.RemoveAll(p => p.RoomId == roomId);
        return Task.CompletedTask;
    }

    private Playlist AddPlaylistInternal(MultiplayerPlaylistItem item, long roomId, int ownerId)
    {
        var playlist = new Playlist
        {
            Id = nextPlaylistId++,
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
        playlists.Add(playlist);
        return playlist;
    }

    private async Task UpdateParticipantCount(long roomId)
    {
        var room = rooms.FirstOrDefault(r => r.Id == roomId);
        if (room != null)
        {
            room.ParticipantCount = participants.Count(p => p.RoomId == roomId && p.LeftAt == null);
        }

        await Task.CompletedTask;
    }

    private async Task EndRoomIfEmpty(long roomId)
    {
        var room = rooms.FirstOrDefault(r => r.Id == roomId);
        if (room == null)
        {
            return;
        }

        if (participants.Any(p => p.RoomId == roomId && p.LeftAt == null))
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