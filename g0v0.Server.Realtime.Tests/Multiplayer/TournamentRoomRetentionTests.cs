// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for tournament room retention: an empty tournament room is kept for a
/// grace period and then destroyed, unless a user rejoins in time.
/// </summary>
[TestFixture]
public class TournamentRoomRetentionTests : MultiplayerTestBase
{
    private string basePath = null!;

    [SetUp]
    public void ConfigureRetentionPeriod()
    {
        basePath = Path.Combine(Path.GetTempPath(), "g0v0-retention-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(basePath, "config"));
        File.WriteAllText(
            Path.Combine(basePath, "config", "realtime.json"),
            "{\"TournamentRoomRetentionPeriod\": \"00:00:00.050\"}");
        RoomFacade.ConfigManager = new ConfigurationManager(basePath);
    }

    [Test]
    public async Task EmptyTournamentRoomIsDestroyedAfterRetentionPeriod()
    {
        var room = await RoomManager.CreateRoom(createTournamentRoom(), USERID, tournamentMode: true);
        await room.JoinRoom(USERID);
        long roomId = room.RoomId;

        Assert.That(RoomManager.GetRoom(roomId), Is.Not.Null);

        Console.WriteLine($"before leave: tournament={room.TournamentMode} users={room.Users.Count}");
        await room.LeaveRoom(USERID);
        Console.WriteLine($"after leave: tournament={room.TournamentMode} endDate={room.EndDate}");

        // The room is retained and an end date is scheduled.
        Assert.That(RoomManager.GetRoom(roomId), Is.Not.Null);
        Assert.That(room.EndDate, Is.Not.Null);

        // After the retention period the in-memory room is destroyed.
        await waitUntil(() => RoomManager.GetRoom(roomId) == null);
        Assert.That(RoomManager.GetRoom(roomId), Is.Null);
    }

    [Test]
    public async Task RejoinCancelsEndDateDestruction()
    {
        // Lengthen the retention period for this test.
        File.WriteAllText(
            Path.Combine(basePath, "config", "realtime.json"),
            "{\"TournamentRoomRetentionPeriod\": \"00:00:00.200\"}");
        RoomFacade.ConfigManager = new ConfigurationManager(basePath);

        var room = await RoomManager.CreateRoom(createTournamentRoom(), USERID, tournamentMode: true);
        await room.JoinRoom(USERID);
        long roomId = room.RoomId;

        await room.LeaveRoom(USERID);
        Assert.That(room.EndDate, Is.Not.Null);

        // A user rejoins before the retention period elapses.
        await Task.Delay(50);
        await room.JoinRoom(USERID);
        Assert.That(room.EndDate, Is.Null);

        // Wait past the original retention period; the room must still exist.
        await Task.Delay(300);
        Assert.That(RoomManager.GetRoom(roomId), Is.Not.Null);
    }

    private static async Task waitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    private static MultiplayerRoom createTournamentRoom() => new(ROOMID)
    {
        Settings = new MultiplayerRoomSettings
        {
            Name = "tournament room",
            MatchType = osu.Game.Online.Rooms.MatchType.HeadToHead,
            QueueMode = QueueMode.HostOnly,
        },
        Playlist = new List<MultiplayerPlaylistItem>
        {
            new()
            {
                OwnerID = 1234,
                BeatmapID = 1234,
                RulesetID = 0,
                RequiredMods = Array.Empty<osu.Game.Online.API.APIMod>(),
                AllowedMods = Array.Empty<osu.Game.Online.API.APIMod>(),
                Freestyle = false,
                Expired = false,
                PlaylistOrder = 0,
                WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
            },
        },
    };
}