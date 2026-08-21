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
        await Hub.JoinRoom(RoomId);
        await ChangeRoomToTeamVersus();

        // The head-to-head -> team-versus conversion assigns the host a team,
        // broadcasting a single MatchUserStateChanged.
        Receiver.Verify(r => r.MatchUserStateChanged(UserId, It.IsAny<MatchUserState>()), Times.Once);

        await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = team });

        CheckUserOnTeam(UserId, team);
        Receiver.Verify(r => r.MatchUserStateChanged(UserId, It.IsAny<MatchUserState>()), Times.Exactly(2));
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
        await Hub.JoinRoom(RoomId);
        await ChangeRoomToTeamVersus();

        await Hub.SendMatchRequest(new SetLockStateRequest { Locked = true });

        int previousTeam = ((TeamVersusUserState)Hub.GetRoom(RoomId)!.Users.Single(u => u.UserID == UserId).MatchState!).TeamID;

        Assert.That(async () => await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 0 }), Throws.TypeOf<InvalidStateException>());

        CheckUserOnTeam(UserId, previousTeam);
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task UserRequestsInvalidTeamChange(int team)
    {
        await Hub.JoinRoom(RoomId);
        await ChangeRoomToTeamVersus();

        // The head-to-head -> team-versus conversion assigns the host a team.
        Receiver.Verify(r => r.MatchUserStateChanged(UserId, It.IsAny<MatchUserState>()), Times.Once);

        int previousTeam = ((TeamVersusUserState)Hub.GetRoom(RoomId)!.Users.Single(u => u.UserID == UserId).MatchState!).TeamID;

        Assert.That(async () => await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = team }), Throws.TypeOf<InvalidStateException>());

        CheckUserOnTeam(UserId, previousTeam);

        // was not called a second time from the invalid change.
        Receiver.Verify(r => r.MatchUserStateChanged(UserId, It.IsAny<MatchUserState>()), Times.Once);
    }

    [Test]
    public async Task NewUsersAssignedToTeamWithFewerUsers()
    {
        await Hub.JoinRoom(RoomId);
        await ChangeRoomToTeamVersus();

        // join a number of users initially to the room
        Dictionary<int, Mock<HubCallerContext>> contexts = new() { [UserId] = ContextUser };

        for (int i = 3; i < 7; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            contexts[i] = context;

            SetUserContext(context);
            await Hub.JoinRoom(RoomId);
        }

        // change all users to team 0
        foreach (Mock<HubCallerContext> context in contexts.Values)
        {
            SetUserContext(context);
            await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 0 });
        }

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users, Is.All.Matches<MultiplayerRoomUser>(u => (u.MatchState as TeamVersusUserState)?.TeamID == 0));

        // all new users should be joined to team 1 to balance the user counts.
        for (int i = 7; i < 12; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            SetUserContext(context);
            await Hub.JoinRoom(RoomId);

            CheckUserOnTeam(i, 1);
        }
    }

    [Test]
    public async Task InitialUsersAssignedToTeamsEqually()
    {
        await Hub.JoinRoom(RoomId);

        // join a number of users initially to the room (still head-to-head)
        for (int i = 3; i < 7; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            SetUserContext(context);
            await Hub.JoinRoom(RoomId);
        }

        // change the match type
        SetUserContext(ContextUser);
        await ChangeRoomToTeamVersus();

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        CheckUserOnTeam(room!.Users[0], 0);
        CheckUserOnTeam(room.Users[1], 1);
        CheckUserOnTeam(room.Users[2], 0);
        CheckUserOnTeam(room.Users[3], 1);
        CheckUserOnTeam(room.Users[4], 0);
    }

    [Test]
    public async Task StateMaintainedBetweenRulesetSwitch()
    {
        await Hub.JoinRoom(RoomId);
        await ChangeRoomToTeamVersus();

        // join a number of users initially to the room
        for (int i = 3; i < 7; i++)
        {
            CreateUser(i, out Mock<HubCallerContext> context, out _);
            SetUserContext(context);
            await Hub.JoinRoom(RoomId);
        }

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        CheckUserOnTeam(room!.Users[0], 0);
        CheckUserOnTeam(room.Users[1], 1);
        CheckUserOnTeam(room.Users[2], 0);
        CheckUserOnTeam(room.Users[3], 1);
        CheckUserOnTeam(room.Users[4], 0);

        // change the match type away and back again
        SetUserContext(ContextUser);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.HeadToHead });
        await ChangeRoomToTeamVersus();

        room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        CheckUserOnTeam(room!.Users[0], 0);
        CheckUserOnTeam(room.Users[1], 1);
        CheckUserOnTeam(room.Users[2], 0);
        CheckUserOnTeam(room.Users[3], 1);
        CheckUserOnTeam(room.Users[4], 0);
    }

    private void CheckUserOnTeam(int userId, int team)
    {
        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        MultiplayerRoomUser user = room!.Users.Single(u => u.UserID == userId);
        Assert.That((user.MatchState as TeamVersusUserState)?.TeamID, Is.EqualTo(team));
    }

    private void CheckUserOnTeam(MultiplayerRoomUser user, int team) =>
        Assert.That((user.MatchState as TeamVersusUserState)?.TeamID, Is.EqualTo(team));

    private async Task ChangeRoomToTeamVersus()
        => await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });
}