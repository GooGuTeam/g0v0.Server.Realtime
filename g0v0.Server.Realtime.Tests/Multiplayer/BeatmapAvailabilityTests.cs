// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for beatmap availability changes, ported from osu-server-spectator's
/// <c>BeatmapAvailabilityTests</c>.
/// </summary>
[TestFixture]
public class BeatmapAvailabilityTests : MultiplayerTestBase
{
    /// <summary>
    /// A second room ID used to verify availability broadcasts stay within the
    /// room the change was made in.
    /// </summary>
    private const long RoomId2 = 9999;

    [Test]
    public void ClientCantChangeAvailabilityWhenNotJoinedRoom()
    {
        Assert.That(() => Hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing()), Throws.TypeOf<NotJoinedRoomException>());
    }

    [Test]
    public async Task AvailabilityChangeBroadcastedOnlyOnChange()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing());
        Receiver.Verify(b => b.UserBeatmapAvailabilityChanged(UserId, It.Is<BeatmapAvailability>(b2 => b2.State == DownloadState.Importing)), Times.Once);

        // should not fire a second time.
        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing());
        Receiver.Verify(b => b.UserBeatmapAvailabilityChanged(UserId, It.Is<BeatmapAvailability>(b2 => b2.State == DownloadState.Importing)), Times.Once);
    }

    [Test]
    public async Task OnlyClientsInSameRoomReceiveAvailabilityChange()
    {
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId2);

        BeatmapAvailability user1Availability = BeatmapAvailability.Importing();
        BeatmapAvailability user2Availability = BeatmapAvailability.Downloading(0.5f);

        SetUserContext(ContextUser);
        await Hub.ChangeBeatmapAvailability(user1Availability);
        Assert.That(Hub.GetRoom(RoomId)?.Users.Single().BeatmapAvailability.Equals(user1Availability), Is.True);

        SetUserContext(ContextUser2);
        await Hub.ChangeBeatmapAvailability(user2Availability);
        Assert.That(Hub.GetRoom(RoomId2)?.Users.Single().BeatmapAvailability.Equals(user2Availability), Is.True);

        Receiver.Verify(c1 => c1.UserBeatmapAvailabilityChanged(UserId, It.Is<BeatmapAvailability>(b => b.Equals(user1Availability))), Times.Once);
        Receiver.Verify(c1 => c1.UserBeatmapAvailabilityChanged(UserId2, It.Is<BeatmapAvailability>(b => b.Equals(user2Availability))), Times.Never);
    }
}