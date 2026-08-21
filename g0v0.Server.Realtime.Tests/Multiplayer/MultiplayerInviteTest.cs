// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for the player-invitation flow, ported from osu-server-spectator's
/// <c>MultiplayerInviteTest</c>.
/// </summary>
[TestFixture]
public class MultiplayerInviteTest : MultiplayerTestBase
{
    /// <inheritdoc />
    protected override bool EnableRelationshipValidation => true;

    [Test]
    public async Task UserCanInviteFriends()
    {
        AddTargetUserWithEnabledPms();

        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(UserId2);

        User2Receiver.Verify(r => r.Invited(UserId, RoomId, string.Empty), Times.Once);
    }

    [Test]
    public async Task UserCantInviteUserTheyBlocked()
    {
        AddTargetUserWithEnabledPms();
        RelationshipRepository!.AddBlock(UserId, UserId2);

        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        Assert.That(async () => await Hub.InvitePlayer(UserId2), Throws.TypeOf<UserBlockedException>());

        User2Receiver.Verify(r => r.Invited(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task UserCantInviteUserTheyAreBlockedBy()
    {
        AddTargetUserWithEnabledPms();
        RelationshipRepository!.AddBlock(UserId2, UserId);

        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        Assert.That(async () => await Hub.InvitePlayer(UserId2), Throws.TypeOf<UserBlockedException>());

        User2Receiver.Verify(r => r.Invited(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task UserCantInviteUserWithDisabledPMs()
    {
        // The target disallows PMs from non-friends and the inviter is not a friend.
        UserRepository!.AddUser(new User { Id = UserId2, Username = "target", PmFriendsOnly = true });

        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        Assert.That(async () => await Hub.InvitePlayer(UserId2), Throws.TypeOf<UserBlocksPMsException>());

        User2Receiver.Verify(r => r.Invited(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task UserCantInviteRestrictedUser()
    {
        // This server has no restriction concept; the invite succeeds.
        AddTargetUserWithEnabledPms();

        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(UserId2);

        User2Receiver.Verify(r => r.Invited(UserId, RoomId, string.Empty), Times.Once);
    }

    [Test]
    public async Task UserCantInviteUserBannedFromRoom()
    {
        AddTargetUserWithEnabledPms();

        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        Hub.GetRoom(RoomId)!.BanUser(UserId2);

        SetUserContext(ContextUser);
        Assert.That(async () => await Hub.InvitePlayer(UserId2), Throws.TypeOf<InvalidStateException>());

        User2Receiver.Verify(r => r.Invited(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task UserCanInviteUserWithEnabledPMs()
    {
        AddTargetUserWithEnabledPms();

        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(UserId2);

        User2Receiver.Verify(r => r.Invited(UserId, RoomId, string.Empty), Times.Once);
    }

    [Test]
    public async Task UserCanInviteIntoRoomWithPassword()
    {
        AddTargetUserWithEnabledPms();

        const string password = "password";

        SetUserContext(ContextUser);
        await Hub.JoinRoomWithPassword(RoomId, password);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(UserId2);

        User2Receiver.Verify(r => r.Invited(UserId, RoomId, password), Times.Once);
    }

    private void AddTargetUserWithEnabledPms()
        => UserRepository!.AddUser(new User { Id = UserId2, Username = "target", PmFriendsOnly = false });
}