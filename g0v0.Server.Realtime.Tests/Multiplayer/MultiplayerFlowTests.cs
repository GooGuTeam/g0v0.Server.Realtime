// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests the full multiplayer match flow, ported from osu-server-spectator's
/// <c>MultiplayerFlowTests</c>.
/// </summary>
[TestFixture]
public class MultiplayerFlowTests : MultiplayerTestBase
{
    [Test]
    public async Task SingleUserMatchFlow()
    {
        await Hub.JoinRoom(ROOMID);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Idle));

        // some users enter a ready state.
        await MarkCurrentUserReadyAndAvailable();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Ready));
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Open));

        // host requests the start of the match.
        await Hub.StartMatch();

        // server requests the all users start loading.
        Receiver.Verify(r => r.LoadRequested(), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.WaitingForLoad), Times.Once);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.WaitingForLoad));

        // all users finish loading.
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.Playing), Times.Once);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Playing));

        // server requests users start playing.
        UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Playing));

        // all users finish playing.
        await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Open));

        // server lets players know that results are ready for consumption (all players have finished).
        Receiver.Verify(r => r.ResultsReady(), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.Results), Times.Once);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Results));

        // players return back to idle state as they please.
        await Hub.ChangeState(MultiplayerUserState.Idle);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Idle));
    }

    [Test]
    public async Task MultiUserMatchFlow()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Idle));

        // both users become ready.
        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(ContextUser2);
        await MarkCurrentUserReadyAndAvailable();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Ready));

        // host requests the start of the match.
        SetUserContext(ContextUser);
        await Hub.StartMatch();

        // server requests the all users start loading.
        Receiver.Verify(r => r.LoadRequested(), Times.Once);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.WaitingForLoad));

        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.WaitingForLoad), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID2, MultiplayerUserState.WaitingForLoad), Times.Once);

        // first user finishes loading.
        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        // room is still waiting for second user to load.
        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        Receiver.Verify(r => r.GameplayStarted(), Times.Never);

        // second user finishes loading, which triggers gameplay to start.
        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Playing));
        UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);
        Assert.That(room.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Playing));
        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.Playing), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID2, MultiplayerUserState.Playing), Times.Once);

        // first user finishes playing.
        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

        room = Hub.GetRoom(ROOMID);

        // room is still waiting for second user to finish playing.
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Playing));
        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.FinishedPlay), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID2, MultiplayerUserState.Playing), Times.Once);
        Receiver.Verify(r => r.ResultsReady(), Times.Never);

        // second user finishes playing.
        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

        room = Hub.GetRoom(ROOMID);

        // server lets players know that results are ready for consumption (all players have finished).
        Receiver.Verify(r => r.ResultsReady(), Times.Once);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Results));
        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.Results), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID2, MultiplayerUserState.Results), Times.Once);

        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Open));
    }

    [Test]
    public async Task SecondUserDoesReceiveLoadRequestWhenMatchRestartedAndNotReady()
    {
        // Start the match initially with both users entering gameplay.
        await Hub.JoinRoom(ROOMID);
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);

        // Finish gameplay for both users.
        SetUserContext(ContextUser2);
        await Hub.AbortGameplay();
        SetUserContext(ContextUser);
        await Hub.AbortGameplay();

        // Restart gameplay with just host being ready.
        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        // Both Host and second user receive it twice.
        UserReceiver.Verify(r => r.LoadRequested(), Times.Exactly(2));
        User2Receiver.Verify(r => r.LoadRequested(), Times.Exactly(2));
    }

    [Test]
    public async Task CanNotVoteToSkipOutsideOfGameplay()
    {
        await Hub.JoinRoom(ROOMID);
        Assert.That(async () => await Hub.VoteToSkipIntro(), Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task VoteToSkip()
    {
        CreateUser(3, out Mock<HubCallerContext> contextUser3, out _);

        // Join all users.
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(contextUser3);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        // Start gameplay
        SetUserContext(ContextUser);
        await Hub.StartMatch();
        await LoadGameplay(ContextUser, ContextUser2, contextUser3);

        // User 1 skips
        SetUserContext(ContextUser);
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

        // User 1 skips again (should be a no-op)
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

        // User 2 skips
        SetUserContext(ContextUser2);
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID2, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Once);

        // User 3 skips (additional messages should be a no-op for clients)
        SetUserContext(contextUser3);
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(3, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Exactly(2));
    }

    // Skipped: osu-server-spectator re-runs the vote-skip check when a user
    // leaves the room (ServerMultiplayerRoom.RemoveUser calls
    // checkVotesToSkipPassed), which can pass the vote for the remaining users.
    // Our RoomBase.RemoveUserInternal does not re-check votes on leave, so the
    // reference assertion `VoteToSkipIntroPassed, Times.Once` after user 3
    // leaves cannot be satisfied. This is a server-side behavioural gap, not a
    // test infra issue.
    [Test]
    public async Task VoteToSkipUpdatesOnLeave()
    {
        CreateUser(3, out Mock<HubCallerContext> contextUser3, out _);
        CreateUser(4, out Mock<HubCallerContext> contextUser4, out _);

        // Join all users.
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(contextUser3);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(contextUser4);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        // Start gameplay
        SetUserContext(ContextUser);
        await Hub.StartMatch();
        await LoadGameplay(ContextUser, ContextUser2, contextUser3, contextUser4);

        // User 1 skips
        SetUserContext(ContextUser);
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

        // User 2 skips
        SetUserContext(ContextUser2);
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID2, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

        // User 3 leaves
        SetUserContext(contextUser3);
        await Hub.LeaveRoom();
        Receiver.Verify(r => r.UserVotedToSkipIntro(3, It.IsAny<bool>()), Times.Never);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Once);
    }

    [Test]
    public async Task VoteToSkipResetOnGameplayState()
    {
        // Join all users.
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        // Start gameplay
        SetUserContext(ContextUser);
        await Hub.StartMatch();
        await LoadGameplay(ContextUser, ContextUser2);

        // User 1 skips
        SetUserContext(ContextUser);
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

        // User 2 skips
        SetUserContext(ContextUser2);
        await Hub.VoteToSkipIntro();
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID2, true), Times.Once);
        Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Once);

        // Start new gameplay session
        SetUserContext(ContextUser);
        await Hub.AbortMatch();
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadGameplay(ContextUser, ContextUser2);

        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID, false), Times.Once);
        Receiver.Verify(r => r.UserVotedToSkipIntro(USERID2, false), Times.Once);
    }
}