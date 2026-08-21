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

        await Hub.JoinRoom(ROOMID);
        Assert.That(Hub.GetRoom(ROOMID)!.Users, Has.Count.EqualTo(1));

        await Hub.OnDisconnectedAsync(null);

        // User is still in the room shortly after disconnecting (grace period).
        await Task.Delay(10);
        Assert.That(Hub.GetRoom(ROOMID)!.Users, Has.Count.EqualTo(1));

        // After the grace period elapses the user is removed from the room; as
        // the last participant, the room is disbanded and dropped from the manager.
        await Task.Delay(200);
        Assert.That(Hub.GetRoom(ROOMID), Is.Null);
    }

    [Test]
    public async Task UserKeptInRoomWhenReconnectingBeforeGracePeriodElapses()
    {
        PlayerManager.MultiplayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(200);

        await Hub.JoinRoom(ROOMID);
        Assert.That(Hub.GetRoom(ROOMID)!.Users, Has.Count.EqualTo(1));

        await Hub.OnDisconnectedAsync(null);

        // Reconnect before the grace period elapses.
        await Task.Delay(50);
        await Hub.OnConnectedAsync();

        // Wait past the original grace period; the user must still be in the room.
        await Task.Delay(250);
        Assert.That(Hub.GetRoom(ROOMID)!.Users, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task HostLeavingRoomAfterGracePeriodTransfersHost()
    {
        PlayerManager.MultiplayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(50);

        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        Assert.That(Hub.GetRoom(ROOMID)!.Host!.UserID, Is.EqualTo(USERID));

        SetUserContext(ContextUser);
        await Hub.OnDisconnectedAsync(null);

        await Task.Delay(200);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users, Has.Count.EqualTo(1));
        Assert.That(room.Host!.UserID, Is.EqualTo(USERID2));
    }
}