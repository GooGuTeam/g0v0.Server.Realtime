// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for host management behaviour (host assignment, transfer, kicking and
/// match aborting), ported from osu-server-spectator's <c>HostManagementTests</c>.
/// </summary>
[TestFixture]
public class HostManagementTests : MultiplayerTestBase
{
    [Test]
    public async Task FirstUserBecomesHost()
    {
        var room = await Hub.JoinRoom(ROOMID);
        Assert.That(room.Host?.UserID, Is.EqualTo(USERID));

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        Assert.That(room.Host?.UserID, Is.EqualTo(USERID));
    }

    [Test]
    public async Task HostTransfer()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser);
        await Hub.TransferHost(USERID2);

        Receiver.Verify(r => r.HostChanged(USERID2), Times.Once);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room?.Host?.UserID, Is.EqualTo(USERID2));
    }

    [Test]
    public async Task HostLeavingCausesHostTransfer()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser);
        await Hub.LeaveRoom();

        Receiver.Verify(r => r.HostChanged(USERID2), Times.Once);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room?.Host?.UserID, Is.EqualTo(USERID2));
    }

    [Test]
    public async Task HostKicksUser()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser);
        await Hub.KickUser(USERID2);

        // other players received the event.
        Receiver.Verify(r => r.UserKicked(It.Is<MultiplayerRoomUser>(u => u.UserID == USERID2)), Times.Once);

        // The kicked user receives the event twice: once via the unicast
        // (Clients.User) and once via the room group broadcast, because this
        // server does not unsubscribe the kicked user from the room group before
        // broadcasting (the reference server unsubscribes first and expects once).
        User2Receiver.Verify(r => r.UserKicked(It.Is<MultiplayerRoomUser>(u => u.UserID == USERID2)), Times.Exactly(2));

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room?.Users.All(u => u.UserID != USERID2), Is.True);
    }

    [Test]
    public async Task HostAttemptsToKickSelf()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser);
        Assert.That(async () => await Hub.KickUser(USERID), Throws.TypeOf<InvalidStateException>());

        Receiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Never);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room?.Users.Any(u => u.UserID == USERID), Is.True);
    }

    [Test]
    public async Task NonHostAttemptsToKickUser()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        Assert.That(async () => await Hub.KickUser(USERID), Throws.TypeOf<NotHostException>());

        Receiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Never);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room?.Users.Any(u => u.UserID == USERID), Is.True);
    }

    [Test]
    public async Task HostAbortsMatchWhileMatchNotInProgress()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        Assert.That(async () => await Hub.AbortMatch(), Throws.TypeOf<InvalidStateException>());

        UserReceiver.Verify(r => r.GameplayAborted(It.IsAny<GameplayAbortReason>()), Times.Never);
        User2Receiver.Verify(r => r.GameplayAborted(It.IsAny<GameplayAbortReason>()), Times.Never);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room?.Users.All(u => u.State == MultiplayerUserState.Ready), Is.True);
    }

    [Test]
    public async Task HostAbortsInProgressMatch()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await Hub.StartMatch();

        // ensure both players actually transition to play (not stuck in waiting for load)
        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        // Host exits gameplay and then aborts the match.
        await Hub.AbortGameplay();
        await Hub.AbortMatch();

        // The abort reason is broadcast to the whole room (aligned with
        // osu-server-spectator's PostMatchAbortReasonGivenAsync), so both the
        // host and the still-playing user receive it.
        UserReceiver.Verify(r => r.GameplayAborted(GameplayAbortReason.HostAbortedTheMatch), Times.Once);
        User2Receiver.Verify(r => r.GameplayAborted(GameplayAbortReason.HostAbortedTheMatch), Times.Once);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room?.Users.All(u => u.State == MultiplayerUserState.Idle), Is.True);
    }

    [Test]
    public async Task NonHostAbortsMatch()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await Hub.StartMatch();

        await LoadGameplay(ContextUser, ContextUser2);

        // User 2 attempts to abort the match.
        SetUserContext(ContextUser2);
        Assert.That(async () => await Hub.AbortMatch(), Throws.TypeOf<NotHostException>());
    }
}