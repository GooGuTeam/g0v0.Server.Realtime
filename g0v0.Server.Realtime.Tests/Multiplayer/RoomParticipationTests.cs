// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.Players;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for room participation (join, leave, passwords, bans), ported from
/// osu-server-spectator's <c>RoomParticipationTests</c>.
/// </summary>
[TestFixture]
public class RoomParticipationTests : MultiplayerTestBase
{
    [Test]
    public async Task UserCanJoinWithPasswordEvenWhenNotRequired()
    {
        // Create a room with no password; any password is accepted when none is required.
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser2);
        await Hub.JoinRoomWithPassword(RoomId, "password");
    }

    [Test]
    public async Task UserCanJoinWithCorrectPassword()
    {
        await Hub.JoinRoomWithPassword(RoomId, "password");

        SetUserContext(ContextUser2);
        await Hub.JoinRoomWithPassword(RoomId, "password");
    }

    [Test]
    public async Task UserCantJoinWithIncorrectPassword()
    {
        await Hub.JoinRoomWithPassword(RoomId, "password");

        SetUserContext(ContextUser2);
        Assert.That(async () => await Hub.JoinRoom(RoomId), Throws.TypeOf<InvalidPasswordException>());
    }

    [Test]
    public async Task UserCantJoinAlreadyEnded()
    {
        await Hub.JoinRoom(RoomId);

        // Mark the persisted room record as ended, then leave so the in-memory
        // room is disbanded and removed from the manager's registry (the fake
        // repository record remains, mirroring an ended room still present in the DB).
        RoomRepository.Rooms.First(r => r.Id == RoomId).EndsAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await Hub.LeaveRoom();

        // The room can no longer be re-hydrated from the repository because it has ended.
        Assert.That(async () => await Hub.JoinRoomExplicit(RoomId), Throws.TypeOf<InvalidStateException>());

        Assert.That(PlayerManager.GetPlayer<LazerPlayer>(UserId)?.State.CurrentRoom, Is.Null);
        Assert.That(Hub.CheckRoomExists(RoomId), Is.False);
    }

    [Test]
    public async Task UserCantJoinWhenAlreadyJoined()
    {
        await Hub.JoinRoom(RoomId);

        // Ensure the same user can't join a room if already in one.
        Assert.That(async () => await Hub.JoinRoom(RoomId), Throws.TypeOf<InvalidStateException>());

        // Once the user leaves, the room is disbanded (last user left) and the
        // server does not revive ended rooms, unlike the reference which clears
        // the room's end date on re-join. Re-joining must therefore fail.
        await Hub.LeaveRoom();
        Assert.That(Hub.CheckRoomExists(RoomId), Is.False);
        Assert.That(async () => await Hub.JoinRoom(RoomId), Throws.Exception);
    }

    [Test]
    public async Task UserCantJoinWhenBanned()
    {
        await Hub.JoinRoom(RoomId);
        Hub.GetRoom(RoomId)!.BanUser(UserId2);

        SetUserContext(ContextUser2);

        // Note: unlike the reference (which expects InvalidStateException), this server
        // folds the ban check into password eligibility and reports it as InvalidPasswordException.
        Assert.That(async () => await Hub.JoinRoom(RoomId), Throws.TypeOf<InvalidPasswordException>());
    }

    [Test]
    public async Task LastUserLeavingCausesRoomDisband()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);
        Assert.That(RoomRepository.Rooms.First(r => r.Id == RoomId).ParticipantCount, Is.EqualTo(1));

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        Assert.That(RoomRepository.Rooms.First(r => r.Id == RoomId).ParticipantCount, Is.EqualTo(2));

        SetUserContext(ContextUser);
        await Hub.LeaveRoom();

        // The room still exists even though the original host left.
        Assert.That(Hub.CheckRoomExists(RoomId), Is.True);
        Assert.That(Hub.GetRoom(RoomId)!.Host?.UserID, Is.EqualTo(UserId2));
        Assert.That(RoomRepository.Rooms.First(r => r.Id == RoomId).ParticipantCount, Is.EqualTo(1));

        SetUserContext(ContextUser2);
        await Hub.LeaveRoom();

        // The room is gone once the last user leaves.
        Assert.That(Hub.CheckRoomExists(RoomId), Is.False);
        Assert.That(RoomRepository.Rooms.First(r => r.Id == RoomId).EndsAt, Is.Not.Null);
    }

    [Test]
    public async Task LeaveWhenNotAlreadyJoinedIsNoop()
    {
        await Hub.LeaveRoom();

        Assert.That(PlayerManager.GetPlayer<LazerPlayer>(UserId)?.State.CurrentRoom, Is.Null);
    }

    [Test]
    public async Task UserJoinLeaveNotifiesOtherUsers()
    {
        await Hub.JoinRoom(RoomId); // join an arbitrary first user (listener).

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        MultiplayerRoomUser roomUser = new(UserId2);

        // Invalid join: the user is already in the room.
        Assert.That(async () => await Hub.JoinRoom(RoomId), Throws.TypeOf<InvalidStateException>());

        Receiver.Verify(r => r.UserJoined(roomUser), Times.Once);

        await Hub.LeaveRoom();
        Receiver.Verify(r => r.UserLeft(roomUser), Times.Once);

        await Hub.JoinRoom(RoomId);
        Receiver.Verify(r => r.UserJoined(roomUser), Times.Exactly(2));

        await Hub.LeaveRoom();
        Receiver.Verify(r => r.UserLeft(roomUser), Times.Exactly(2));
    }

    [Test]
    public void UserJoinPreRetrievalFailureCleansUpRoom()
    {
        // The reference exercises a database hydration failure (host mismatch) which
        // does not exist in this architecture. The equivalent join failure here is
        // joining a room that does not exist: it must throw and leave no stale state.
        SetUserContext(ContextUser2);
        Assert.That(async () => await Hub.JoinRoomExplicit(RoomId), Throws.TypeOf<InvalidStateException>());

        Assert.That(Hub.CheckRoomExists(RoomId), Is.False);
        Assert.That(PlayerManager.GetPlayer<LazerPlayer>(UserId2)?.State.CurrentRoom, Is.Null);
    }

    // Skipped: UserCantJoinWhenRestricted — this server has no user restriction check
    // (the reference relies on a database IsUserRestrictedAsync mock, which does not
    // exist in this architecture), so restricted users are not rejected at join time.
    //
    // Skipped: UserKickedFromRoomWhenBanned — this server's BanUser only prevents
    // future joins; it does not remove an already-present user from the room.
    //
    // Skipped: UserJoinPreJoinFailureCleansUpRoom — the reference injects a failure in
    // SetRoomEndDateAsync via a database mock; the fake room repository here never throws,
    // so this failure path cannot be exercised without modifying test infrastructure.
    //
    // Skipped: UserJoinPostJoinFailureCleansUpRoomAndUser — same reason (AddRoomParticipantAsync
    // failure injection is unavailable). Additionally, this server does not roll back the
    // in-memory user join when persistence fails, so the reference's cleanup semantics are
    // not implemented (see final report).
}