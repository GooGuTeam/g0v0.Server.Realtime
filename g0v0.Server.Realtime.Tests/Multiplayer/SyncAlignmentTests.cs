// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Objects.Rooms;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Rooms;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for behaviours aligned with osu-server-spectator: join rollback,
/// duplicate-settings short-circuit, unknown request handling, referee
/// permissions and manual team locking.
/// </summary>
[TestFixture]
public class SyncAlignmentTests : MultiplayerTestBase
{
    [Test]
    public async Task FailedJoinRollsBackInMemoryState()
    {
        await Hub.JoinRoom(RoomId);

        // Enable the persistence failure before the second user joins.
        RoomRepository.FailAddUserToRoom = true;

        SetUserContext(ContextUser2);
        Assert.That(
            async () => await Hub.JoinRoom(RoomId),
            Throws.TypeOf<InvalidOperationException>());

        // The failed joiner must not remain in the room.
        IRoom room = Hub.GetRoom(RoomId)!;
        Assert.That(room.Users.Any(u => u.UserID == UserId2), Is.False);
        Assert.That(room.Host!.UserID, Is.EqualTo(UserId));
    }

    [Test]
    public void FailedJoinDoesNotLeaveHostIfHostJoinFails()
    {
        // Host joins a brand-new room but persistence fails: the room must not
        // keep a phantom host.
        RoomRepository.FailAddUserToRoom = true;

        Assert.That(
            async () => await Hub.JoinRoom(RoomId),
            Throws.TypeOf<InvalidOperationException>());

        IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Null);
    }

    [Test]
    public async Task ResubmittingSameSettingsIsNoOp()
    {
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();

        IRoom room = Hub.GetRoom(RoomId)!;
        MultiplayerRoomSettings original = room.Settings;
        MultiplayerRoomSettings settings = new()
        {
            Name = original.Name,
            MatchType = original.MatchType,
            QueueMode = original.QueueMode,
            MaxParticipants = original.MaxParticipants,
            AutoStartDuration = original.AutoStartDuration,
            AutoSkip = original.AutoSkip,
            Password = original.Password,
        };

        Receiver.Invocations.Clear();

        await Hub.ChangeSettings(settings);

        // No settings broadcast, and the user stays ready.
        Receiver.Verify(r => r.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()), Times.Never);
        Assert.That(room.Users.Single().State, Is.EqualTo(MultiplayerUserState.Ready));
    }

    [Test]
    public async Task UnknownMatchUserRequestIsIgnored()
    {
        await Hub.JoinRoom(RoomId);

        // A team-versus request sent to a head-to-head room is silently ignored.
        Assert.DoesNotThrowAsync(async () => await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 0 }));
    }

    [Test]
    public async Task RefereeCanChangeSettings()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        // Promote user 2 to referee.
        IRoom room = Hub.GetRoom(RoomId)!;
        room.Users.First(u => u.UserID == UserId2).Role = MultiplayerRoomUserRole.Referee;

        SetUserContext(ContextUser2);
        Assert.DoesNotThrowAsync(async () => await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "referee renamed",
            MatchType = OsuMatchType.HeadToHead,
            QueueMode = QueueMode.HostOnly,
        }));
        Assert.That(room.Settings.Name, Is.EqualTo("referee renamed"));
    }

    [Test]
    public async Task RefereeCanAbortMatch()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await Hub.StartMatch();

        // Promote user 2 to referee and abort as referee.
        IRoom room = Hub.GetRoom(RoomId)!;
        room.Users.First(u => u.UserID == UserId2).Role = MultiplayerRoomUserRole.Referee;

        SetUserContext(ContextUser2);
        Assert.DoesNotThrowAsync(async () => await Hub.AbortMatch());
        Assert.That(room.Users.All(u => u.State == MultiplayerUserState.Idle), Is.True);
    }

    [Test]
    public async Task RefereeCanTransferHost()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);
        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        IRoom room = Hub.GetRoom(RoomId)!;
        room.Users.First(u => u.UserID == UserId2).Role = MultiplayerRoomUserRole.Referee;

        SetUserContext(ContextUser2);
        await Hub.TransferHost(UserId2);

        Assert.That(room.Host!.UserID, Is.EqualTo(UserId2));
    }

    [Test]
    public async Task TeamChangeAllowedDuringGameplayWhenNotManuallyLocked()
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
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadGameplay(ContextUser, ContextUser2);

        // Without a manual lock, teams can still change during gameplay.
        IRoom room = Hub.GetRoom(RoomId)!;
        int currentTeam = ((TeamVersusUserState)room.Users.First(u => u.UserID == UserId).MatchState!).TeamID;
        int otherTeam = currentTeam == 0 ? 1 : 0;

        SetUserContext(ContextUser);
        Assert.DoesNotThrowAsync(async () => await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = otherTeam }));
        Assert.That(((TeamVersusUserState)room.Users.First(u => u.UserID == UserId).MatchState!).TeamID, Is.EqualTo(otherTeam));
    }

    [Test]
    public async Task TeamChangeRejectedWhenManuallyLocked()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "team room",
            MatchType = OsuMatchType.TeamVersus,
            QueueMode = QueueMode.HostOnly,
            MaxParticipants = 16,
        });

        // Manually lock the room.
        await Hub.SendMatchRequest(new SetLockStateRequest { Locked = true });

        Assert.That(
            async () => await Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 1 }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task FailedHandleUserJoinedRollsBackParticipantAndBroadcastsUserLeft()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "team room",
            MatchType = OsuMatchType.TeamVersus,
            QueueMode = QueueMode.HostOnly,
            MaxParticipants = 16,
        });

        // Make the team-versus match-state broadcast fail during HandleUserJoined
        // of the second user, after the participant row was persisted.
        Receiver.Setup(r => r.MatchUserStateChanged(It.IsAny<int>(), It.IsAny<MatchUserState>()))
                .ThrowsAsync(new InvalidOperationException("Simulated HandleUserJoined failure."));

        SetUserContext(ContextUser2);
        Assert.That(
            async () => await Hub.JoinRoom(RoomId),
            Throws.TypeOf<InvalidOperationException>());

        IRoom room = Hub.GetRoom(RoomId)!;

        // The failed joiner is not in memory, not in the database participants,
        // and a compensating UserLeft was broadcast to existing clients.
        Assert.That(room.Users.Any(u => u.UserID == UserId2), Is.False);
        Assert.That(RoomRepository.GetParticipants(RoomId).Any(p => p is { UserId: UserId2, LeftAt: null }), Is.False);
        Receiver.Verify(r => r.UserLeft(It.Is<MultiplayerRoomUser>(u => u.UserID == UserId2)), Times.Once);
    }

    [Test]
    public void CreateRoom_InstanceConstructionFailure_DeletesOrphanedDatabaseRoom()
    {
        // An unsupported match type passes mod validation but fails in
        // CreateRoomInstance; the persisted database room must be cleaned up.
        MultiplayerRoom request = new(0)
        {
            Settings = new MultiplayerRoomSettings
            {
                Name = "orphan room",
                MatchType = OsuMatchType.Matchmaking,
                QueueMode = QueueMode.HostOnly,
            },
        };

        Assert.That(
            async () => await RoomManager.CreateRoom(request, UserId),
            Throws.TypeOf<InvalidStateException>());

        Assert.That(RoomRepository.Rooms, Is.Empty);
        Assert.That(RoomManager.GetAllRooms(), Is.Empty);
    }

    [Test]
    public async Task FailedSubscriptionUnsubscribesAndRollsBackJoin()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(RoomId);

        // Simulate a group subscription failure for the second user's join.
        Groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("Simulated subscription failure."));

        SetUserContext(ContextUser2);
        Assert.That(
            async () => await Hub.JoinRoom(RoomId),
            Throws.TypeOf<InvalidOperationException>());

        // The failed joiner was removed from the room and the subscription was
        // compensated with a removal from the group.
        IRoom room = Hub.GetRoom(RoomId)!;
        Assert.That(room.Users.Any(u => u.UserID == UserId2), Is.False);
        Groups.Verify(g => g.RemoveFromGroupAsync(It.IsAny<string>(), MultiplayerEventDispatcher.GetGroupId(RoomId), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task QueueModeChangeReordersQueueImmediately()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "round robin room",
            MatchType = OsuMatchType.HeadToHead,
            QueueMode = QueueMode.AllPlayersRoundRobin,
        });

        // Add several items in round-robin mode (host owns all of them).
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 200,
            BeatmapChecksum = "checksum2",
        });
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 300,
            BeatmapChecksum = "checksum3",
        });

        IRoom room = Hub.GetRoom(RoomId)!;
        int[] before = room.Playlist.OrderBy(i => i.PlaylistOrder).Select(i => i.BeatmapID).ToArray();

        // Switch to host-only mode: the queue is reordered immediately.
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "round robin room",
            MatchType = OsuMatchType.HeadToHead,
            QueueMode = QueueMode.HostOnly,
        });

        int[] after = room.Playlist.OrderBy(i => i.PlaylistOrder).Select(i => i.BeatmapID).ToArray();
        Assert.That(after, Is.EquivalentTo(before));

        // The current item is repointed to the first non-expired item under the
        // new ordering (the seeded item with the lowest order).
        Assert.That(room.Settings.PlaylistItemId, Is.EqualTo(room.Playlist.OrderBy(i => i.PlaylistOrder).First().ID));
    }

    [Test]
    public async Task JoinRejectedWhenEndDateHasPassed()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = "tournament room",
            MatchType = OsuMatchType.HeadToHead,
            QueueMode = QueueMode.HostOnly,
            MaxParticipants = 16,
        });

        // Simulate an end date which has already passed (destruction pending):
        // the room must reject new joins.
        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        room.SetEndDateForTesting(DateTimeOffset.UtcNow.AddMinutes(-1));

        SetUserContext(ContextUser2);
        Assert.That(
            async () => await Hub.JoinRoom(RoomId),
            Throws.TypeOf<InvalidPasswordException>());
    }
}