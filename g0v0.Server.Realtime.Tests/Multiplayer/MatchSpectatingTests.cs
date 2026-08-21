// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for spectating behaviour, ported from osu-server-spectator's
/// <c>MatchSpectatingTests</c>.
/// </summary>
[TestFixture]
public class MatchSpectatingTests : MultiplayerTestBase
{
    [Test]
    public async Task CanTransitionBetweenIdleAndSpectating()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeState(MultiplayerUserState.Spectating);
        await Hub.ChangeState(MultiplayerUserState.Idle);
    }

    [Test]
    public async Task CanTransitionFromReadyToSpectating()
    {
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.ChangeState(MultiplayerUserState.Spectating);
    }

    [Test]
    public async Task SpectatingUserStateDoesNotChange()
    {
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeState(MultiplayerUserState.Spectating);

        SetUserContext(ContextUser);

        await Hub.StartMatch();
        Receiver.Verify(r => r.LoadRequested(), Times.Once);
        Clients.Verify(clients => clients.User(UserId2.ToString()).UserStateChanged(UserId2, MultiplayerUserState.WaitingForLoad), Times.Never);

        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);
        Clients.Verify(clients => clients.User(UserId2.ToString()).UserStateChanged(UserId2, MultiplayerUserState.Playing), Times.Never);

        await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
        Receiver.Verify(r => r.ResultsReady(), Times.Once);
        Clients.Verify(clients => clients.User(UserId2.ToString()).UserStateChanged(UserId2, MultiplayerUserState.Results), Times.Never);
    }

    [Test]
    public async Task SpectatingHostCanStartMatch()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeState(MultiplayerUserState.Spectating);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await Hub.StartMatch();
        Receiver.Verify(r => r.LoadRequested(), Times.Once);
    }

    [Test]
    public async Task SpectatingUserReceivesLoadRequestedAfterGameplayStarted()
    {
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        Receiver.Verify(r => r.LoadRequested(), Times.Once);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeState(MultiplayerUserState.Spectating);
        User2Receiver.Verify(r => r.LoadRequested(), Times.Once);

        // Ensure no other clients received LoadRequested().
        Receiver.Verify(r => r.LoadRequested(), Times.Once);
    }
}