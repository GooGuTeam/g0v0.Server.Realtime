// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Objects.Rooms;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for the room manager: creation, loading, match-type switching and
/// registry lifecycle.
/// </summary>
[TestFixture]
public class RoomManagerTests : MultiplayerTestBase
{
    [Test]
    public async Task CreateRoom_RegistersRoomAndReturnsInstance()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId);

        Assert.That(room, Is.TypeOf<HeadToHeadRoom>());
        Assert.That(RoomRepository.Rooms.Single().Source, Is.EqualTo(ServerIdentify.Realtime));
        Assert.That(RoomManager.GetRoom(room.RoomId), Is.SameAs(room));
        Assert.That(RoomManager.GetAllRooms().ToArray(), Has.Length.EqualTo(1));
    }

    [TestCase(OsuMatchType.Playlists)]
    [TestCase(OsuMatchType.Matchmaking)]
    [TestCase(OsuMatchType.RankedPlay)]
    public void CreateRoom_UnsupportedMatchType_Throws(OsuMatchType matchType)
    {
        MultiplayerRoom request = CreateRoomRequest();
        request.Settings.MatchType = matchType;

        Assert.That(
            async () => await RoomManager.CreateRoom(request, UserId),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public void CreateRoom_FreestyleItemWithAllowedMods_Throws()
    {
        MultiplayerRoom request = CreateRoomRequest();
        request.Playlist[0].Freestyle = true;
        request.Playlist[0].AllowedMods = [new APIMod { Acronym = "HR" }];

        Assert.That(
            async () => await RoomManager.CreateRoom(request, UserId),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task CreateRoom_TournamentMode_IsPersisted()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId, tournamentMode: true);

        Assert.That(room.TournamentMode, Is.True);
    }

    [Test]
    public async Task GetOrLoadRoom_LoadsExistingRoomFromDatabase()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId);
        RoomManager.RemoveRoom(room.RoomId);

        Assert.That(RoomManager.GetRoom(room.RoomId), Is.Null);

        IRoom? loaded = await RoomManager.GetOrLoadRoom(room.RoomId);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RoomId, Is.EqualTo(room.RoomId));
    }

    [TestCase(ServerIdentify.Stable)]
    [TestCase(ServerIdentify.Lazer)]
    public async Task GetOrLoadRoom_ForeignRoom_ReturnsNullWithoutChangingIt(ServerIdentify source)
    {
        Common.Database.Models.Room foreign = await RoomRepository.CreateRoom(CreateRoomRequest(), UserId, source);
        DateTimeOffset endDate = DateTimeOffset.UtcNow.AddMinutes(30);
        await RoomRepository.SetRoomEndDate(foreign.Id, endDate);

        Assert.That(await RoomManager.GetOrLoadRoom(foreign.Id), Is.Null);
        Assert.That(RoomManager.GetAllRooms(), Is.Empty);
        Assert.That(foreign.EndsAt, Is.EqualTo(endDate));
        Assert.That(foreign.ChannelId, Is.Zero);
        Assert.That(foreign.Source, Is.EqualTo(source));
    }

    [Test]
    public async Task GetOrLoadRoom_UnknownRoom_ReturnsNull()
    {
        Assert.That(await RoomManager.GetOrLoadRoom(999999), Is.Null);
    }

    [Test]
    public async Task GetOrLoadRoom_EndedRoom_ReturnsNull()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId);
        long roomId = room.RoomId;

        // End the room in the database.
        await RoomRepository.SetRoomEndDate(roomId, DateTimeOffset.UtcNow.AddMinutes(-1));
        RoomManager.RemoveRoom(roomId);

        Assert.That(await RoomManager.GetOrLoadRoom(roomId), Is.Null);
    }

    [Test]
    public async Task GetOrLoadRoom_FutureEndDate_LoadsRoomAndClearsEndDate()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId, tournamentMode: true);
        long roomId = room.RoomId;

        // A future end date (empty tournament room awaiting destruction).
        await RoomRepository.SetRoomEndDate(roomId, DateTimeOffset.UtcNow.AddMinutes(30));
        RoomManager.RemoveRoom(roomId);

        IRoom? loaded = await RoomManager.GetOrLoadRoom(roomId);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RoomId, Is.EqualTo(roomId));

        // The future end date is cleared so the room can be joined again.
        Common.Database.Models.Room dbRoom = RoomRepository.Rooms.First(r => r.Id == roomId);
        Assert.That(dbRoom.EndsAt, Is.Null);
    }

    [Test]
    public async Task ChangeMatchType_SwapsRoomInstance()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId);
        long roomId = room.RoomId;
        room.ChannelId = 12345;

        IRoom changed = await RoomManager.ChangeMatchType(roomId, OsuMatchType.TeamVersus);

        Assert.That(changed, Is.TypeOf<TeamVersusRoom>());
        Assert.That(changed.RoomId, Is.EqualTo(roomId));
        Assert.That(changed.ChannelId, Is.EqualTo(12345));
        Assert.That(RoomManager.GetRoom(roomId), Is.SameAs(changed));
        Assert.That(RoomManager.GetRoom(roomId)!.Settings.MatchType, Is.EqualTo(OsuMatchType.TeamVersus));
    }

    [Test]
    public async Task ChangeMatchType_ReplacesInstanceCreatedByPreviousChange()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId);
        long roomId = room.RoomId;

        IRoom teamVersus = await RoomManager.ChangeMatchType(roomId, OsuMatchType.TeamVersus);
        await teamVersus.ChangeRoomSettings(new MultiplayerRoomSettings
        {
            Name = teamVersus.Settings.Name,
            MatchType = OsuMatchType.HeadToHead,
            QueueMode = teamVersus.Settings.QueueMode,
            MaxParticipants = teamVersus.Settings.MaxParticipants,
        });

        IRoom? changedBack = RoomManager.GetRoom(roomId);
        Assert.That(changedBack, Is.TypeOf<HeadToHeadRoom>());
        Assert.That(changedBack!.Settings.MatchType, Is.EqualTo(OsuMatchType.HeadToHead));
    }

    [Test]
    public async Task ChangeMatchType_UnsupportedMatchType_ThrowsWithoutChangingRoom()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId);
        long roomId = room.RoomId;

        Assert.That(
            async () => await RoomManager.ChangeMatchType(roomId, OsuMatchType.Playlists),
            Throws.TypeOf<InvalidStateException>());

        Assert.That(RoomManager.GetRoom(roomId), Is.SameAs(room));
        Assert.That(room.Settings.MatchType, Is.EqualTo(OsuMatchType.HeadToHead));
        Assert.That(RoomRepository.Rooms.First(r => r.Id == roomId).Type, Is.EqualTo(OsuMatchType.HeadToHead));
    }

    [Test]
    public void ChangeMatchType_UnknownRoom_Throws()
    {
        Assert.That(
            async () => await RoomManager.ChangeMatchType(999999, OsuMatchType.TeamVersus),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task RemoveRoom_UnregistersRoom()
    {
        IRoom room = await RoomManager.CreateRoom(CreateRoomRequest(), UserId);
        long roomId = room.RoomId;

        RoomManager.RemoveRoom(roomId);

        Assert.That(RoomManager.GetRoom(roomId), Is.Null);
    }

    private static MultiplayerRoom CreateRoomRequest() => new(0)
    {
        Settings = new MultiplayerRoomSettings
        {
            Name = "test room",
            MatchType = OsuMatchType.HeadToHead,
            QueueMode = QueueMode.HostOnly,
        },
        Playlist = new List<MultiplayerPlaylistItem>
        {
            new()
            {
                OwnerID = 1234,
                BeatmapID = 1234,
                RulesetID = 0,
                RequiredMods = [],
                AllowedMods = [],
                Freestyle = false,
                Expired = false,
                PlaylistOrder = 0,
                WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
            },
        },
    };
}