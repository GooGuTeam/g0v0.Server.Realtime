// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for room settings, ported from osu-server-spectator's
/// <c>RoomSettingsTests</c>.
/// </summary>
[TestFixture]
public class RoomSettingsTests : MultiplayerTestBase
{
    [Test]
    public async Task ChangingSettingsUpdatesModel()
    {
        MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
        {
            Name = "bestest room ever",
            MatchType = OsuMatchType.HeadToHead,
        };

        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(testSettings);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.Name, Is.EqualTo(testSettings.Name));
    }

    [Test]
    public async Task ChangingSettingsMarksReadyUsersAsIdle()
    {
        MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
        {
            Name = "bestest room ever",
            MatchType = OsuMatchType.HeadToHead,
        };

        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeState(MultiplayerUserState.Ready);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeState(MultiplayerUserState.Ready);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.Ready), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID2, MultiplayerUserState.Ready), Times.Once);
        Assert.That(room!.Users, Is.All.Matches<MultiplayerRoomUser>(u => u.State == MultiplayerUserState.Ready));

        SetUserContext(ContextUser);
        await Hub.ChangeSettings(testSettings);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Receiver.Verify(r => r.UserStateChanged(USERID, MultiplayerUserState.Idle), Times.Once);
        Receiver.Verify(r => r.UserStateChanged(USERID2, MultiplayerUserState.Idle), Times.Once);
        Assert.That(room!.Users, Is.All.Matches<MultiplayerRoomUser>(u => u.State == MultiplayerUserState.Idle));

        // Check that both users start gameplay - the second user also starts despite being in an idle state.
        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Ready);
        await Hub.StartMatch();

        UserReceiver.Verify(r => r.LoadRequested(), Times.Once);
        User2Receiver.Verify(r => r.LoadRequested(), Times.Once);
    }

    [Test]
    public void UserCantChangeSettingsWhenNotJoinedRoom()
    {
        Assert.That(() => Hub.ChangeSettings(new MultiplayerRoomSettings()), Throws.TypeOf<NotJoinedRoomException>());
    }

    [Test]
    public async Task UserCantChangeSettingsWhenGameIsActive()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeState(MultiplayerUserState.Ready);
        await Hub.StartMatch();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));

        Assert.That(async () => await Hub.ChangeSettings(new MultiplayerRoomSettings()), Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task RoomSettingsUpdateNotifiesOtherUsers()
    {
        MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
        {
            Password = "password",
            MatchType = OsuMatchType.HeadToHead,
        };

        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(testSettings);
        Receiver.Verify(r => r.SettingsChanged(testSettings), Times.Once);
    }

    [Test]
    public async Task ChangingSettingsToUnsupportedMatchTypeThrows()
    {
        await Hub.JoinRoom(ROOMID);

        Assert.That(
            () => Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                MatchType = OsuMatchType.Playlists,
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task ServerDoesNotAcceptClientPlaylistItemId()
    {
        long playlistItemId = (await Hub.JoinRoom(ROOMID)).Settings.PlaylistItemId;

        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "bestest room ever",
            PlaylistItemId = 1,
        });

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.Settings.PlaylistItemId, Is.EqualTo(playlistItemId));
        Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
    }

    [Test]
    public async Task ChangingQueueModeUpdatesModel()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            QueueMode = QueueMode.AllPlayers,
        });

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.QueueMode, Is.EqualTo(QueueMode.AllPlayers));
    }
}