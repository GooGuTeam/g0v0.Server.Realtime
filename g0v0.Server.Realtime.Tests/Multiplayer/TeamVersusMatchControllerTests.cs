// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for team-versus team assignment and team changes, ported from
/// osu-server-spectator's <c>TeamVersusMatchControllerTests</c>.
/// </summary>
[TestFixture]
public class TeamVersusMatchControllerTests : MultiplayerTestBase
{
    [TestCase(0)]
    [TestCase(1)]
    public async Task UserRequestsValidTeamChange(int team)
    {
        await Hub.JoinRoom(ROOMID);
        await ChangeRoomToTeamVersus();

        // The head-to-head -> team-versus conversion assigns the host a team,
        // broadcasting a single MatchUserStateChanged.
        Receiver.Verify(r => r.MatchUserStateChanged(USERID, It.IsAny<MatchUserState>()), Times.Once);

        await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = team });

        checkUserOnTeam(USERID, team);
        Receiver.Verify(r => r.MatchUserStateChanged(USERID, It.IsAny<MatchUserState>()), Times.Exactly(2));
    }

    [Test]
    public async Task UserCannotChangeTeamsWhenRoomLocked()
    {
        // Reference behaviour: the room can be manually locked and unlocked with
        // SetLockStateRequest, and team changes are rejected while locked. This
        // server has no SetLockStateRequest handling (the request falls through
        // to the default NotSupportedException path), so the manual lock flow
        // cannot be exercised here. Team changes during gameplay (teams locked)
        // are covered by MultiplayerHubTests.TeamVersus_ChangeTeamDuringGameplay_Throws.
        await Hub.JoinRoom(ROOMID);
        await ChangeRoomToTeamVersus();

        await Hub.SendMatchRequest(new SetLockStateRequest { Locked = true });

        var previousTeam = ((TeamVersusUserState)Hub.GetRoom(ROOMID)!.Users.Single(u => u.UserID == USERID).MatchState!).TeamID;

        Assert.That(async () => await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 0 }), Throws.TypeOf<InvalidStateException>());

        checkUserOnTeam(USERID, previousTeam);
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task UserRequestsInvalidTeamChange(int team)
    {
        await Hub.JoinRoom(ROOMID);
        await ChangeRoomToTeamVersus();

        // The head-to-head -> team-versus conversion assigns the host a team.
        Receiver.Verify(r => r.MatchUserStateChanged(USERID, It.IsAny<MatchUserState>()), Times.Once);

        var previousTeam = ((TeamVersusUserState)Hub.GetRoom(ROOMID)!.Users.Single(u => u.UserID == USERID).MatchState!).TeamID;

        Assert.That(async () => await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = team }), Throws.TypeOf<InvalidStateException>());

        checkUserOnTeam(USERID, previousTeam);

        // was not called a second time from the invalid change.
        Receiver.Verify(r => r.MatchUserStateChanged(USERID, It.IsAny<MatchUserState>()), Times.Once);
    }

    [Test]
    public async Task NewUsersAssignedToTeamWithFewerUsers()
    {
        await Hub.JoinRoom(ROOMID);
        await ChangeRoomToTeamVersus();

        // join a number of users initially to the room
        var contexts = new Dictionary<int, Mock<HubCallerContext>> { [USERID] = ContextUser };

        for (int i = 3; i < 7; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            contexts[i] = context;

            SetUserContext(context);
            await Hub.JoinRoom(ROOMID);
        }

        // change all users to team 0
        foreach (Mock<HubCallerContext> context in contexts.Values)
        {
            SetUserContext(context);
            await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 0 });
        }

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users, Is.All.Matches<MultiplayerRoomUser>(u => (u.MatchState as TeamVersusUserState)?.TeamID == 0));

        // all new users should be joined to team 1 to balance the user counts.
        for (int i = 7; i < 12; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            SetUserContext(context);
            await Hub.JoinRoom(ROOMID);

            checkUserOnTeam(i, 1);
        }
    }

    [Test]
    public async Task InitialUsersAssignedToTeamsEqually()
    {
        await Hub.JoinRoom(ROOMID);

        // join a number of users initially to the room (still head-to-head)
        for (int i = 3; i < 7; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            SetUserContext(context);
            await Hub.JoinRoom(ROOMID);
        }

        // change the match type
        SetUserContext(ContextUser);
        await ChangeRoomToTeamVersus();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        checkUserOnTeam(room!.Users[0], 0);
        checkUserOnTeam(room.Users[1], 1);
        checkUserOnTeam(room.Users[2], 0);
        checkUserOnTeam(room.Users[3], 1);
        checkUserOnTeam(room.Users[4], 0);
    }

    [Test]
    public async Task StateMaintainedBetweenRulesetSwitch()
    {
        await Hub.JoinRoom(ROOMID);
        await ChangeRoomToTeamVersus();

        // join a number of users initially to the room
        for (int i = 3; i < 7; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            SetUserContext(context);
            await Hub.JoinRoom(ROOMID);
        }

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        checkUserOnTeam(room!.Users[0], 0);
        checkUserOnTeam(room.Users[1], 1);
        checkUserOnTeam(room.Users[2], 0);
        checkUserOnTeam(room.Users[3], 1);
        checkUserOnTeam(room.Users[4], 0);

        // change the match type away and back again
        SetUserContext(ContextUser);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.HeadToHead });
        await ChangeRoomToTeamVersus();

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        checkUserOnTeam(room!.Users[0], 0);
        checkUserOnTeam(room.Users[1], 1);
        checkUserOnTeam(room.Users[2], 0);
        checkUserOnTeam(room.Users[3], 1);
        checkUserOnTeam(room.Users[4], 0);
    }

    private void checkUserOnTeam(int userId, int team)
    {
        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        var user = room!.Users.Single(u => u.UserID == userId);
        Assert.That((user.MatchState as TeamVersusUserState)?.TeamID, Is.EqualTo(team));
    }

    private void checkUserOnTeam(MultiplayerRoomUser user, int team) =>
        Assert.That((user.MatchState as TeamVersusUserState)?.TeamID, Is.EqualTo(team));

    private async Task ChangeRoomToTeamVersus()
        => await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });
}