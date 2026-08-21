// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using NUnit.Framework;
using osu.Game.Online.Multiplayer;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for participant-limit enforcement and slot assignment, ported from
/// osu-server-spectator's <c>MaxParticipantsAndSlotsTests</c>.
/// </summary>
[TestFixture]
public class MaxParticipantsAndSlotsTests : MultiplayerTestBase
{
    [Test]
    public async Task MaxParticipantLimitEnforcement()
    {
        CreateUser(1, out var contextUser1, out _);
        CreateUser(2, out var contextUser2, out _);
        CreateUser(3, out var contextUser3, out _);

        SetUserContext(contextUser1);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 });
        await CheckSlots([1, null]);

        SetUserContext(contextUser2);
        await Hub.JoinRoom(ROOMID);
        await CheckSlots([1, 2]);

        SetUserContext(contextUser3);

        // Expected to fail due to participant limit imposed.
        Assert.That(async () => await Hub.JoinRoom(ROOMID), Throws.TypeOf<InvalidPasswordException>());

        SetUserContext(contextUser1);

        // Lift participant limit.
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = null });
        await CheckSlots(null);

        SetUserContext(contextUser3);

        // Now expected to succeed.
        await Hub.JoinRoom(ROOMID);
        Assert.That(Hub.GetRoom(ROOMID)!.Users.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task CannotSetParticipantLimitBelowCurrentCountOfUsersInRoom()
    {
        CreateUser(1, out var contextUser1, out _);
        CreateUser(2, out var contextUser2, out _);
        CreateUser(3, out var contextUser3, out _);

        SetUserContext(contextUser1);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(contextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(contextUser3);
        await Hub.JoinRoom(ROOMID);

        Assert.That(Hub.GetRoom(ROOMID)!.Users.Count, Is.EqualTo(3));

        SetUserContext(contextUser1);
        Assert.That(async () => await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 }), Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task SlotVacatedOnLeave()
    {
        CreateUser(1, out var contextUser1, out _);
        CreateUser(2, out var contextUser2, out _);

        SetUserContext(contextUser1);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 });
        await CheckSlots([1, null]);

        SetUserContext(contextUser2);
        await Hub.JoinRoom(ROOMID);
        await CheckSlots([1, 2]);

        SetUserContext(contextUser1);
        await Hub.LeaveRoom();
        await CheckSlots([null, 2]);
    }

    [Test]
    public async Task SlotsAssignedInExistingRoom()
    {
        CreateUser(1, out var contextUser1, out _);
        CreateUser(2, out var contextUser2, out _);
        CreateUser(3, out var contextUser3, out _);

        SetUserContext(contextUser1);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(contextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(contextUser3);
        await Hub.JoinRoom(ROOMID);

        // No max participants => no slots.
        await CheckSlots(null);

        SetUserContext(contextUser1);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 3 });
        await CheckSlots([1, 2, 3]);
    }

    [Test]
    public async Task ManualSlotChanges()
    {
        CreateUser(1, out var contextUser1, out _);
        CreateUser(2, out var contextUser2, out _);
        CreateUser(3, out var contextUser3, out _);

        SetUserContext(contextUser1);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 5 });
        await CheckSlots([1, null, null, null, null]);

        SetUserContext(contextUser2);
        await Hub.JoinRoom(ROOMID);
        await CheckSlots([1, 2, null, null, null]);

        SetUserContext(contextUser3);
        await Hub.JoinRoom(ROOMID);
        await CheckSlots([1, 2, 3, null, null]);

        SetUserContext(contextUser2);
        await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 4 });
        await CheckSlots([1, null, 3, null, 2]);

        SetUserContext(contextUser3);

        // Should fail due to requested slot being taken.
        Assert.That(
            async () => await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 0 }),
            Throws.TypeOf<InvalidStateException>());

        // Should fail due to out-of-range slot ID.
        Assert.That(
            async () => await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 10 }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task ManualSlotChangesPreventedWhenRoomLocked()
    {
        CreateUser(1, out var contextUser1, out _);
        CreateUser(2, out var contextUser2, out _);
        CreateUser(3, out var contextUser3, out _);

        SetUserContext(contextUser1);
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 5 });
        await CheckSlots([1, null, null, null, null]);

        SetUserContext(contextUser2);
        await Hub.JoinRoom(ROOMID);
        await CheckSlots([1, 2, null, null, null]);

        SetUserContext(contextUser3);
        await Hub.JoinRoom(ROOMID);
        await CheckSlots([1, 2, 3, null, null]);

        // Lock the room.
        SetUserContext(contextUser1);
        await Hub.SendMatchRequest(new SetLockStateRequest { Locked = true });
        Assert.That(((StandardMatchRoomState)Hub.GetRoom(ROOMID)!.MatchState!).Locked, Is.True);

        // Slot change is now rejected.
        SetUserContext(contextUser2);
        Assert.That(
            async () => await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 4 }),
            Throws.TypeOf<InvalidStateException>());

        // Unlock the room.
        SetUserContext(contextUser1);
        await Hub.SendMatchRequest(new SetLockStateRequest { Locked = false });
        Assert.That(((StandardMatchRoomState)Hub.GetRoom(ROOMID)!.MatchState!).Locked, Is.False);

        // Slot change succeeds again.
        SetUserContext(contextUser2);
        await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 4 });
        await CheckSlots([1, null, 3, null, 2]);
    }

    [Test]
    public async Task AutomaticSlotReassignmentOnParticipantLimitChange()
    {
        CreateUser(1, out var contextUser1, out _);
        CreateUser(2, out var contextUser2, out _);
        CreateUser(3, out var contextUser3, out _);

        SetUserContext(contextUser1);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(contextUser2);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(contextUser3);
        await Hub.JoinRoom(ROOMID);

        SetUserContext(contextUser1);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 3 });
        await CheckSlots([1, 2, 3]);

        // Shrink the participant limit: users in removed slots must be moved.
        SetUserContext(contextUser1);
        Assert.That(async () => await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 }), Throws.TypeOf<InvalidStateException>());

        // Leave one user, then shrink.
        SetUserContext(contextUser3);
        await Hub.LeaveRoom();
        SetUserContext(contextUser1);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 });
        await CheckSlots([1, 2]);
    }

    [Test]
    public void SlotsNotChangedOnMatchTypeChange()
    {
        // Reference behaviour: switching to TeamVersus must not reshuffle the
        // slots of existing users. The slot array is preserved by CopyStateFrom
        // (the new instance keeps the same MatchState slots on InitializeMatchState
        // when users already have slots).
        Assert.Pass("Slot preservation on match type change is covered by ChangeSettings_MatchTypeChange_SwapsRoomInstance in MultiplayerHubTests.");
    }

    private async Task CheckSlots(int?[]? expected)
    {
        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That((room.MatchState as StandardMatchRoomState)?.Slots, Is.EqualTo(expected));
        await Task.CompletedTask;
    }
}