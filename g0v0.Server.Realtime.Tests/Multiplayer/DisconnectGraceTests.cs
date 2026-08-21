// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for the multiplayer disconnect grace period: a disconnected user is
/// kept in their room for a grace period and only removed if they do not
/// reconnect in time.
/// </summary>
[TestFixture]
public class DisconnectGraceTests : MultiplayerTestBase
{
    [Test]
    public async Task UserRemovedFromRoomAfterGracePeriodElapses()
    {
        PlayerManager.MultiplayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(50);

        await Hub.JoinRoom(RoomId);
        Assert.That(Hub.GetRoom(RoomId)!.Users, Has.Count.EqualTo(1));

        await Hub.OnDisconnectedAsync(exception: null);

        // User is still in the room shortly after disconnecting (grace period).
        await Task.Delay(10);
        Assert.That(Hub.GetRoom(RoomId)!.Users, Has.Count.EqualTo(1));

        // After the grace period elapses the user is removed from the room; as
        // the last participant, the room is disbanded and dropped from the manager.
        await Task.Delay(200);
        Assert.That(Hub.GetRoom(RoomId), Is.Null);
    }

    [Test]
    public async Task UserKeptInRoomWhenReconnectingBeforeGracePeriodElapses()
    {
        PlayerManager.MultiplayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(200);

        await Hub.JoinRoom(RoomId);
        Assert.That(Hub.GetRoom(RoomId)!.Users, Has.Count.EqualTo(1));

        await Hub.OnDisconnectedAsync(exception: null);

        // Reconnect before the grace period elapses.
        await Task.Delay(50);
        await Hub.OnConnectedAsync();

        // Wait past the original grace period; the user must still be in the room.
        await Task.Delay(250);
        Assert.That(Hub.GetRoom(RoomId)!.Users, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task HostLeavingRoomAfterGracePeriodTransfersHost()
    {
        PlayerManager.MultiplayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(50);

        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        Assert.That(Hub.GetRoom(RoomId)!.Host!.UserID, Is.EqualTo(UserId));

        SetUserContext(ContextUser);
        await Hub.OnDisconnectedAsync(exception: null);

        await Task.Delay(200);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room!.Users, Has.Count.EqualTo(1));
        Assert.That(room.Host!.UserID, Is.EqualTo(UserId2));
    }
}