// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for user state management in multiplayer rooms, ported from
/// osu-server-spectator's <c>UserStateManagementTests</c>.
/// </summary>
[TestFixture]
public class UserStateManagementTests : MultiplayerTestBase
{
    [Test]
    public async Task UserStateChangeNotifiesOtherUsers()
    {
        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();
        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.Ready), Times.Once);
    }

    [TestCase(MultiplayerUserState.WaitingForLoad)]
    [TestCase(MultiplayerUserState.Playing)]
    [TestCase(MultiplayerUserState.Results)]
    public async Task UserCantChangeStateToReservedStates(MultiplayerUserState reservedState)
    {
        await Hub.JoinRoom(ROOMID);
        Assert.That(async () => await Hub.ChangeState(reservedState), Throws.TypeOf<InvalidStateChangeException>());
    }

    [Test]
    public async Task StartingMatchWithNoReadyUsersFails()
    {
        await Hub.JoinRoom(ROOMID);
        Assert.That(async () => await Hub.StartMatch(), Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task StartingMatchWithHostNotReadyFails()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        Assert.That(async () => await Hub.StartMatch(), Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task StartingAlreadyStartedMatchFails()
    {
        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Open));

        await Hub.StartMatch();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        Assert.That(async () => await Hub.StartMatch(), Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task AllUsersBackingOutFromLoadCancelsTransitionToPlay()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        await Hub.AbortGameplay();
        SetUserContext(ContextUser2);
        await Hub.AbortGameplay();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Open));
    }

    // Skipped: the reference test additionally asserts database effects
    // (MarkPlaylistItemAsPlayedAsync once, game_aborted log event once,
    // game_completed never) which this server has no database mock for.
    // The state transitions are covered by the test below.
    [Test]
    public async Task AllUsersAbortingGameplayIndividuallyLogsGameAsAbortedAndExpiresPlaylistItem()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        long playlistItemId = Hub.GetRoom(ROOMID)!.Playlist.Single(i => i.ID == Hub.GetRoom(ROOMID)!.Settings.PlaylistItemId).ID;

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        SetUserContext(ContextUser);
        await Hub.AbortGameplay();
        SetUserContext(ContextUser2);
        await Hub.AbortGameplay();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Open));

        Assert.That(room!.Playlist.Single(i => i.ID == playlistItemId).Expired, Is.True);
    }

    [Test]
    public async Task HostAbortingGameplayBeforeStartLogsGameAsAbortedAndExpiresPlaylistItem()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        long playlistItemId = Hub.GetRoom(ROOMID)!.Playlist.Single(i => i.ID == Hub.GetRoom(ROOMID)!.Settings.PlaylistItemId).ID;

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        SetUserContext(ContextUser);
        await Hub.AbortMatch();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Open));

        Assert.That(room!.Playlist.Single(i => i.ID == playlistItemId).Expired, Is.True);
    }

    [Test]
    public async Task HostAbortingGameplayAfterStartLogsGameAsAbortedAndExpiresPlaylistItem()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        long playlistItemId = room.Playlist.Single(i => i.ID == room.Settings.PlaylistItemId).ID;

        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Playing));
        Assert.That(room!.Users.Select(u => u.State).ToArray(), Is.All.EqualTo(MultiplayerUserState.Playing));

        await Hub.AbortMatch();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Open));

        // The item which was being played is expired (HandleGameplayCompleted).
        Assert.That(room!.Playlist.Single(i => i.ID == playlistItemId).Expired, Is.True);
    }

    [TestCase(null)]
    [TestCase(DownloadState.Unknown)]
    [TestCase(DownloadState.Downloading)]
    [TestCase(DownloadState.NotDownloaded)]
    public async Task UsersWithoutBeatmapWillNotEnterGameplay(DownloadState? state)
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        // user 2 is not ready and doesn't have the beatmap.
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        if (state.HasValue)
        {
            await Hub.ChangeBeatmapAvailability(new BeatmapAvailability(state.Value));
        }

        SetUserContext(ContextUser);
        await Hub.StartMatch();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        Assert.That(room.Users.Count(u => u.State == MultiplayerUserState.WaitingForLoad), Is.EqualTo(1));
        Assert.That(room.Users.Count(u => u.State == MultiplayerUserState.Idle), Is.EqualTo(1));

        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Count(u => u.State == MultiplayerUserState.Playing), Is.EqualTo(1));
        Assert.That(room.Users.Count(u => u.State == MultiplayerUserState.Idle), Is.EqualTo(1));
    }

    [Test]
    public async Task BothReadyAndIdleUsersTransitionToPlay()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        // user 2 is not ready but has the beatmap. should join gameplay.
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

        SetUserContext(ContextUser);
        await Hub.StartMatch();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));
        Assert.That(room.Users.Count(u => u.State == MultiplayerUserState.WaitingForLoad), Is.EqualTo(2));

        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Count(u => u.State == MultiplayerUserState.Playing), Is.EqualTo(2));
    }

    [Test]
    public async Task UserDisconnectsDuringGameplayUpdatesRoomState()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await Hub.StartMatch();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));
        Assert.That(room.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.WaitingForLoad));

        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Playing));
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Playing));

        // first user exits gameplay
        SetUserContext(ContextUser);
        await Hub.AbortGameplay();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Playing));

        // second user gets disconnected
        SetUserContext(ContextUser2);
        await Hub.LeaveRoom();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.Open));
    }

    [Test]
    public async Task OnlyFinishedUsersTransitionToResults()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

        SetUserContext(ContextUser);

        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser, ContextUser2);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Count(u => u.State == MultiplayerUserState.Results), Is.EqualTo(2));
    }

    [Test]
    public async Task IdleUsersDoGetLoadRequest()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

        SetUserContext(ContextUser);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Select(u => u.State), Is.All.EqualTo(MultiplayerUserState.Idle));

        // one user enters a ready state.
        await MarkCurrentUserReadyAndAvailable();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Count(u => u.State == MultiplayerUserState.Idle), Is.EqualTo(1));
        Assert.That(room.Users.Count(u => u.State == MultiplayerUserState.Ready), Is.EqualTo(1));

        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Open));

        // host requests the start of the match.
        await Hub.StartMatch();

        Receiver.Verify(r => r.LoadRequested(), Times.Once);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.All(u => u.State == MultiplayerUserState.WaitingForLoad), Is.True);
    }

    // Ported with a divergence: the reference test expects that a user in
    // FinishedPlay can switch back to Idle. Here the server's ChangeUserState
    // silently ignores Idle only while the user is in a gameplay state; once
    // FinishedPlay is broadcast the room immediately moves the user to Results
    // (not a gameplay state), so the subsequent Idle transition succeeds exactly
    // as in the reference. The WaitingForLoad and Playing phases are asserted as
    // in the reference.
    [Test]
    public async Task UserCanNotSwitchToIdleDuringGameplay()
    {
        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        // Test during WaitingForLoad state.
        await Hub.ChangeState(MultiplayerUserState.Idle);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users[0].State, Is.EqualTo(MultiplayerUserState.WaitingForLoad));

        // Test during Playing state.
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users[0].State, Is.EqualTo(MultiplayerUserState.Playing));

        // Test during FinishedPlay state (allows switching to idle).
        await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users[0].State, Is.EqualTo(MultiplayerUserState.Idle));
    }

    [Test]
    public async Task UserSwitchedToIdleWhenAbortingGameplay()
    {
        await Hub.JoinRoom(ROOMID);

        // Test during WaitingForLoad state.
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await Hub.AbortGameplay();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users[0].State, Is.EqualTo(MultiplayerUserState.Idle));
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Open));

        // Test during Playing state.
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.AbortGameplay();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users[0].State, Is.EqualTo(MultiplayerUserState.Idle));
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Open));
    }

    [Test]
    public async Task CanNotAbortGameplayInNonGameplayStates()
    {
        await Hub.JoinRoom(ROOMID);
        Assert.That(async () => await Hub.AbortGameplay(), Throws.TypeOf<InvalidStateException>());

        await MarkCurrentUserReadyAndAvailable();
        Assert.That(async () => await Hub.AbortGameplay(), Throws.TypeOf<InvalidStateException>());
    }

    // Ported with a divergence: the reference expects the kicked user to
    // receive UserKicked once (unicast, since the reference unsubscribes the
    // kicked user from the room group before broadcasting). Our server does not
    // unsubscribe before broadcasting, so the kicked user receives the event
    // twice (unicast + room group), as already documented in HostManagementTests.
    // The host receives the event once via the room group.
    [Test]
    public async Task KickUser()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser);
        await Hub.KickUser(USERID2);

        UserReceiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Once);
        User2Receiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Exactly(2));
        UserReceiver.Verify(r => r.UserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Never);
        User2Receiver.Verify(r => r.UserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Never);
    }

    // Skipped: the reference asserts that disconnecting a user immediately
    // leaves the room (OnDisconnectedAsync → CleanUpState → RoomController.LeaveRoom).
    // This server keeps the user in the room for a 30-second grace period
    // (MultiplayerHub.OnDisconnectedAsync → PlayerManager.StartMultiplayerDisconnectGrace)
    // so a brief connection drop does not eject them; the removal happens only
    // after the grace period elapses without a reconnect. Deliberate server
    // divergence, not a test infra issue.
    [Test]
    public async Task LeaveOnDisconnect()
    {
        PlayerManager.MultiplayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(50);

        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        await Hub.OnDisconnectedAsync(null);

        // The user is still in the room during the grace period.
        Assert.That(Hub.GetRoom(ROOMID)!.Users, Has.Count.EqualTo(2));

        // After the grace period elapses without a reconnect, the user leaves the room.
        await Task.Delay(200);
        Receiver.Verify(r => r.UserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Once);
    }
}