// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests covering propagation of match-type specific state through the room's
/// event hooks (<see cref="g0v0.Server.Realtime.Objects.Rooms.RoomBase"/>'s
/// <c>HandleUserJoined</c> / <c>InitializeMatchState</c>), ported from
/// osu-server-spectator's <c>MatchTypeRoomEventHookTests</c>.
/// </summary>
/// <remarks>
/// The reference verifies hook invocations on a mock <c>IMatchController</c>;
/// this server has no such controller, so the hooks are observed through their
/// client-visible effects: team-versus rooms assign each joining user a team
/// and broadcast <c>MatchUserStateChanged</c> / <c>MatchRoomStateChanged</c>.
/// </remarks>
[TestFixture]
public class MatchTypeRoomEventHookTests : MultiplayerTestBase
{
    [Test]
    public async Task NewUserJoinedTriggersMatchTypeHook()
    {
        // Join the host and switch the room to team-versus so that joining
        // users are assigned teams by the match-type hook (HandleUserJoined).
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "team room",
            MatchType = OsuMatchType.TeamVersus,
            QueueMode = QueueMode.HostOnly,
            MaxParticipants = 16,
        });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        // The team-versus hook must have assigned the newly joined user a team
        // and broadcast the match user-state change to the room.
        Receiver.Verify(r => r.MatchUserStateChanged(UserId2, It.IsAny<TeamVersusUserState>()), Times.Once);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Single(u => u.UserID == UserId2).MatchState, Is.TypeOf<TeamVersusUserState>());
    }

    [Test]
    public async Task UserLeavesTriggersMatchTypeHook()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "team room",
            MatchType = OsuMatchType.TeamVersus,
            QueueMode = QueueMode.HostOnly,
            MaxParticipants = 16,
        });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        // Both users occupy slots (MaxParticipants = 16).
        int?[] slots = ((StandardMatchRoomState)Hub.GetRoom(RoomId)!.MatchState!).Slots!;
        Assert.That(slots, Does.Contain(UserId));
        Assert.That(slots, Does.Contain(UserId2));

        SetUserContext(ContextUser2);
        await Hub.LeaveRoom();

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.Any(u => u.UserID == UserId2), Is.False);

        // The leaving user's slot is released by the user-leave hook.
        slots = ((StandardMatchRoomState)room.MatchState!).Slots!;
        Assert.That(slots, Does.Not.Contain(UserId2));
        Assert.That(slots, Does.Contain(UserId));
    }

    [Test]
    public async Task TypeChangeTriggersInitialMatchTypeHook()
    {
        CreateUser(3, out Mock<HubCallerContext> contextUser3, out _);
        CreateUser(4, out Mock<HubCallerContext> contextUser4, out _);
        CreateUser(5, out Mock<HubCallerContext> contextUser5, out _);

        // Join a number of users to the room (the host plus four guests).
        await Hub.JoinRoom(RoomId);
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        SetUserContext(contextUser3);
        await Hub.JoinRoom(RoomId);
        SetUserContext(contextUser4);
        await Hub.JoinRoom(RoomId);
        SetUserContext(contextUser5);
        await Hub.JoinRoom(RoomId);

        // Change the match type to team-versus.
        SetUserContext(ContextUser);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "team room",
            MatchType = OsuMatchType.TeamVersus,
            QueueMode = QueueMode.HostOnly,
            MaxParticipants = 16,
        });

        // The match-type hook must have been invoked for every already-joined
        // user, assigning each a valid team and broadcasting the change.
        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users, Has.Count.EqualTo(5));
        Assert.That(room.Users.Select(u => u.MatchState).OfType<TeamVersusUserState>().Count(), Is.EqualTo(5));
        Assert.That(room.Users.Select(u => ((TeamVersusUserState)u.MatchState!).TeamID), Is.All.InRange(0, 1));
        Assert.That(room.MatchState, Is.TypeOf<TeamVersusRoomState>());

        foreach (int userId in new[] { UserId, UserId2, 3, 4, 5 })
        {
            Receiver.Verify(r => r.MatchUserStateChanged(userId, It.IsAny<TeamVersusUserState>()), Times.Once);
        }
    }
}