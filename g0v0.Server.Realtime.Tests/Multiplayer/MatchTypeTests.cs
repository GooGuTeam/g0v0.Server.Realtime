// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.Rooms;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for match-type specific behaviour, ported from osu-server-spectator's
/// <c>MatchTypeTests</c>.
/// </summary>
[TestFixture]
public class MatchTypeTests : MultiplayerTestBase
{
    [Test]
    public async Task MatchRoomStateUpdatePropagatesToUsers()
    {
        await Hub.JoinRoom(ROOMID);

        // g0v0's IRoom.MatchState has a protected setter and the event
        // dispatcher is not exposed to tests, so the room state change is
        // triggered through the room's InitializeMatchState (on match-type
        // change), which broadcasts MatchRoomStateChanged to the room group.
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });

        Receiver.Verify(r => r.MatchRoomStateChanged(It.IsAny<TeamVersusRoomState>()), Times.Once);
    }

    [Test]
    public async Task MatchEventPropagatesToUsers()
    {
        await Hub.JoinRoom(ROOMID);

        // The event dispatcher is not exposed to tests, so transient match
        // events are exercised through the hub's countdown flow: starting and
        // stopping a countdown broadcasts CountdownStartedEvent /
        // CountdownStoppedEvent to the room group via MatchEvent.
        await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown is MatchStartCountdown)), Times.Once);

        int countdownId = room!.ActiveCountdowns.OfType<MatchStartCountdown>().Single().ID;
        await Hub.SendMatchRequest(new StopCountdownRequest(countdownId));

        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdownId)), Times.Once);
    }

    [Test]
    public async Task MatchUserStateUpdatePropagatesToUsers()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        int initialTeam = ((TeamVersusUserState)room!.Users.Single(u => u.UserID == USERID2).MatchState!).TeamID;

        // A real team change updates the user's match state and broadcasts
        // MatchUserStateChanged to the room group. The broadcast carries the
        // shared (mutated) user state instance, so the change is observed on the
        // room side and the propagation via the second broadcast.
        await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = initialTeam == 0 ? 1 : 0 });

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(((TeamVersusUserState)room!.Users.Single(u => u.UserID == USERID2).MatchState!).TeamID, Is.EqualTo(initialTeam == 0 ? 1 : 0));

        // One broadcast on join (initial team assignment) plus one for the change.
        Receiver.Verify(r => r.MatchUserStateChanged(USERID2, It.IsAny<TeamVersusUserState>()), Times.Exactly(2));
    }

    [Test]
    public async Task MatchUserRequestForwardsToController()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });

        // g0v0 has no IMatchController; MatchUserRequest is forwarded to the
        // room's match-type specific handler (HandleMatchUserRequest), observed
        // here through its effect on the requesting user's team state.
        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        int currentTeam = ((TeamVersusUserState)room!.Users.Single().MatchState!).TeamID;
        await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = currentTeam == 0 ? 1 : 0 });

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(((TeamVersusUserState)room!.Users.Single().MatchState!).TeamID, Is.EqualTo(currentTeam == 0 ? 1 : 0));
    }

    [Test]
    public async Task ChangeMatchType()
    {
        await Hub.JoinRoom(ROOMID);

        // initially default.
        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.MatchType, Is.EqualTo(OsuMatchType.HeadToHead));

        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.MatchType, Is.EqualTo(OsuMatchType.TeamVersus));

        Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
        Receiver.Verify(r => r.MatchUserStateChanged(USERID, It.IsAny<TeamVersusUserState>()), Times.Once);
    }

    [Test]
    public async Task JoinRoomWithTypeCreatesCorrectInstance()
    {
        // g0v0 has no database mock to seed a room of a given type, so the
        // match type is set through the hub and the resulting room instance is
        // verified to be of the correct implementation type.
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.MatchType, Is.EqualTo(OsuMatchType.TeamVersus));
        Assert.That(room, Is.TypeOf<TeamVersusRoom>());
        Assert.That(room.MatchState, Is.TypeOf<TeamVersusRoomState>());
    }

    [Test]
    public async Task ExistingUserInformedOfNewUserStateCorrectOrder()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });

        var callOrder = new List<string>();
        Receiver.Setup(r => r.UserJoined(It.IsAny<MultiplayerRoomUser>())).Callback(() => callOrder.Add("UserJoined"));
        Receiver.Setup(r => r.MatchUserStateChanged(It.IsAny<int>(), It.IsAny<MatchUserState>())).Callback(() => callOrder.Add("MatchUserStateChanged"));

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        // The joining user's UserJoined must be broadcast before their newly
        // assigned match user state, so existing clients see the user first.
        string[] expectedOrder = ["UserJoined", "MatchUserStateChanged"];
        Assert.That(callOrder, Is.EqualTo(expectedOrder));
    }
}