// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

#pragma warning disable SA1117

using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.Rooms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Rooms;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Hubs;

[TestFixture]
public class MultiplayerHubTests
{
    [Test]
    public async Task CreateRoom_AsHost_ReturnsSnapshotAndJoinsGroup()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));

        Assert.Multiple(() =>
        {
            Assert.That(room.RoomID, Is.GreaterThan(0));
            Assert.That(room.Settings.MatchType, Is.EqualTo(OsuMatchType.HeadToHead));
            Assert.That(room.Users, Has.Count.EqualTo(1));
            Assert.That(room.Users[0].UserID, Is.EqualTo(1));
            Assert.That(room.Host?.UserID, Is.EqualTo(1));

            // The host joins before subscribing to the room's event group, so
            // they do not receive their own UserJoined (which would play the
            // player-joined sound on the client).
            Assert.That(host.Recorder.UserJoinedEvents, Has.Count.EqualTo(0));
            Assert.That(host.GroupManager.AddedGroups, Has.Count.EqualTo(1));

            // The host must never receive a UserLeft for their own join.
            Assert.That(host.Recorder.UserLeftEvents, Has.Count.EqualTo(0));

            // Settings.PlaylistItemId must point at a valid playlist item so the
            // client's CurrentPlaylistItem (Single()) never throws.
            Assert.That(room.Playlist, Has.Count.EqualTo(1));
            Assert.That(room.Settings.PlaylistItemId, Is.EqualTo(room.Playlist[0].ID));
            Assert.That(room.Playlist.Single(item => item.ID == room.Settings.PlaylistItemId), Is.Not.Null);
        });
    }

    [Test]
    public async Task JoinRoom_WithMatchingPassword_AddsUserAndBroadcasts()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead, password: "secret"));
        host.Recorder.Clear();
        host.GroupManager.Reset();

        MultiplayerRoom joined = await guest.Hub.JoinRoomWithPassword(room.RoomID, "secret");

        Assert.Multiple(() =>
        {
            Assert.That(joined.Users, Has.Count.EqualTo(2));

            // The host (already subscribed) receives the guest's UserJoined.
            Assert.That(host.Recorder.UserJoinedEvents, Has.Count.EqualTo(1));
            Assert.That(host.Recorder.UserJoinedEvents[0].UserID, Is.EqualTo(2));

            // The guest joins before subscribing, so they do not receive their
            // own UserJoined (no player-joined sound on the client).
            Assert.That(guest.Recorder.UserJoinedEvents, Has.Count.EqualTo(0));
        });
    }

    [Test]
    public void JoinRoom_WithWrongPassword_ThrowsInvalidPassword()
    {
        Assert.Pass("covered by JoinRoomWithPassword_WithWrongPassword_Throws");
    }

    [Test]
    public async Task JoinRoomWithPassword_WithWrongPassword_ThrowsInvalidPassword()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead, password: "secret"));

        Assert.That(
            async () => await guest.Hub.JoinRoomWithPassword(room.RoomID, "wrong"),
            Throws.TypeOf<InvalidPasswordException>());
    }

    [Test]
    public async Task ChangeState_ReadyThenStart_StartsMatch()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        // Both users ready.
        await host.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await guest.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await host.Hub.ChangeState(MultiplayerUserState.Ready);
        await guest.Hub.ChangeState(MultiplayerUserState.Ready);

        await host.Hub.StartMatch();

        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;
        Assert.Multiple(() =>
        {
            Assert.That(realRoom.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));
            Assert.That(host.Recorder.RoomStates, Does.Contain(MultiplayerRoomState.WaitingForLoad));
            Assert.That(host.Recorder.LoadRequestedEvents, Has.Count.EqualTo(1));
            Assert.That(guest.Recorder.LoadRequestedEvents, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task CompleteLoadHandshake_StartsGameplay()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);
        await host.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await guest.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await host.Hub.ChangeState(MultiplayerUserState.Ready);
        await guest.Hub.ChangeState(MultiplayerUserState.Ready);
        await host.Hub.StartMatch();

        await host.Hub.ChangeState(MultiplayerUserState.Loaded);
        await host.Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        await guest.Hub.ChangeState(MultiplayerUserState.Loaded);
        await guest.Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;
        Assert.Multiple(() =>
        {
            Assert.That(realRoom.State, Is.EqualTo(MultiplayerRoomState.Playing));
            Assert.That(host.Recorder.GameplayStartedEvents, Has.Count.EqualTo(1));
            Assert.That(guest.Recorder.GameplayStartedEvents, Has.Count.EqualTo(1));
            Assert.That(host.Recorder.RoomStates, Does.Contain(MultiplayerRoomState.Playing));
        });
    }

    [Test]
    public async Task FinishPlay_AllUsers_ReturnsToOpen()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);
        await host.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await guest.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await host.Hub.ChangeState(MultiplayerUserState.Ready);
        await guest.Hub.ChangeState(MultiplayerUserState.Ready);
        await host.Hub.StartMatch();
        await host.Hub.ChangeState(MultiplayerUserState.Loaded);
        await host.Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        await guest.Hub.ChangeState(MultiplayerUserState.Loaded);
        await guest.Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        // Finish play for both.
        await host.Hub.ChangeState(MultiplayerUserState.FinishedPlay);
        await guest.Hub.ChangeState(MultiplayerUserState.FinishedPlay);

        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;
        Assert.Multiple(() =>
        {
            Assert.That(realRoom.State, Is.EqualTo(MultiplayerRoomState.Open));
            Assert.That(host.Recorder.ResultsReadyEvents, Has.Count.EqualTo(1));
            Assert.That(guest.Recorder.ResultsReadyEvents, Has.Count.EqualTo(1));
            Assert.That(host.Recorder.RoomStates, Does.Contain(MultiplayerRoomState.Open));
        });
    }

    [Test]
    public async Task SpectatingUser_ReceivesLoadRequested_OnMatchStart()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle spectator = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await spectator.Hub.JoinRoom(room.RoomID);

        // The spectator enters spectate mode.
        await spectator.Hub.ChangeState(MultiplayerUserState.Spectating);

        // Host readies and starts the match.
        await host.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await host.Hub.ChangeState(MultiplayerUserState.Ready);
        await host.Hub.StartMatch();

        Assert.Multiple(() =>
        {
            // The spectator must receive LoadRequested so the client can enter
            // the multiplayer spectator screen.
            Assert.That(spectator.Recorder.LoadRequestedEvents, Has.Count.EqualTo(1));
            Assert.That(host.Recorder.LoadRequestedEvents, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task SpectatingUser_ExitingSpectate_CanReady()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        await guest.Hub.ChangeState(MultiplayerUserState.Spectating);
        Assert.That(room.Users.First(u => u.UserID == 2).State, Is.EqualTo(MultiplayerUserState.Spectating));

        // Exiting spectate returns the user to Idle, after which they can ready.
        await guest.Hub.ChangeState(MultiplayerUserState.Idle);
        Assert.That(room.Users.First(u => u.UserID == 2).State, Is.EqualTo(MultiplayerUserState.Idle));

        await guest.Hub.ChangeState(MultiplayerUserState.Ready);
        Assert.That(room.Users.First(u => u.UserID == 2).State, Is.EqualTo(MultiplayerUserState.Ready));
    }

    [Test]
    public async Task TeamVersus_JoinAssignsTeams_AndChangeTeamWorks()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guestA = await environment.ConnectAsync(2, "conn-guest-a");
        TestHubHandle guestB = await environment.ConnectAsync(3, "conn-guest-b");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.TeamVersus));
        await guestA.Hub.JoinRoom(room.RoomID);
        await guestB.Hub.JoinRoom(room.RoomID);

        // Host and first guest on team 0/1 respectively.
        TeamVersusUserState? hostState = room.Users.First(u => u.UserID == 1).MatchState as TeamVersusUserState;
        TeamVersusUserState? guestAState = room.Users.First(u => u.UserID == 2).MatchState as TeamVersusUserState;
        TeamVersusUserState? guestBState = room.Users.First(u => u.UserID == 3).MatchState as TeamVersusUserState;

        Assert.Multiple(() =>
        {
            Assert.That(hostState, Is.Not.Null);
            Assert.That(guestAState, Is.Not.Null);
            Assert.That(guestBState, Is.Not.Null);
            Assert.That(
                new[] { hostState!.TeamID, guestAState!.TeamID, guestBState!.TeamID },
                Is.All.InRange(0, 1));
            Assert.That(hostState.TeamID, Is.Not.EqualTo(guestAState!.TeamID));
            Assert.That(room.MatchState, Is.TypeOf<TeamVersusRoomState>());
        });

        // Change team of guest A to the team of guest B.
        await guestA.Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = guestBState!.TeamID });
        TeamVersusUserState? updated = room.Users.First(u => u.UserID == 2).MatchState as TeamVersusUserState;
        Assert.That(updated!.TeamID, Is.EqualTo(guestBState.TeamID));
    }

    [Test]
    public async Task TeamVersus_ChangeTeamDuringGameplay_AllowedWithoutManualLock()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.TeamVersus));
        await guest.Hub.JoinRoom(room.RoomID);
        await host.Hub.ChangeState(MultiplayerUserState.Ready);
        await guest.Hub.ChangeState(MultiplayerUserState.Ready);
        await host.Hub.StartMatch();

        // Without a manual lock, team changes remain allowed during gameplay
        // (aligned with the reference, which only checks State.Locked).
        Assert.DoesNotThrowAsync(async () => await guest.Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 0 }));

        // A manually locked room rejects team changes even outside gameplay.
        await host.Hub.AbortMatch();
        await host.Hub.SendMatchRequest(new SetLockStateRequest { Locked = true });
        Assert.That(
            async () => await guest.Hub.SendMatchRequest(new ChangeTeamRequest { TeamID = 0 }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task TransferHost_AsHost_UpdatesHost()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        await host.Hub.TransferHost(2);

        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;
        Assert.Multiple(() =>
        {
            Assert.That(realRoom.Host?.UserID, Is.EqualTo(2));
            Assert.That(host.Recorder.HostChangedEvents, Does.Contain(2));
        });
    }

    [Test]
    public async Task KickUser_AsHost_RemovesUserAndBroadcasts()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);
        host.Recorder.Clear();

        await host.Hub.KickUser(2);

        Assert.Multiple(() =>
        {
            Assert.That(room.Users.Any(u => u.UserID == 2), Is.False);
            Assert.That(host.Recorder.UserKickedEvents, Has.Count.EqualTo(1));

            // The kicked user receives the unicast kick plus the group broadcast.
            Assert.That(guest.Recorder.UserKickedEvents, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ChangeSettings_MatchTypeChange_SwapsRoomInstance()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        await host.Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = room.Settings.Name,
            MatchType = OsuMatchType.TeamVersus,
            QueueMode = room.Settings.QueueMode,
        });

        IRoom swapped = environment.RoomManager.GetRoom(room.RoomID)!;
        Assert.Multiple(() =>
        {
            Assert.That(swapped, Is.TypeOf<TeamVersusRoom>());
            Assert.That(swapped.Settings.MatchType, Is.EqualTo(OsuMatchType.TeamVersus));
            Assert.That(swapped.MatchState, Is.TypeOf<TeamVersusRoomState>());
            Assert.That(swapped.Users, Has.Count.EqualTo(2));
            Assert.That(swapped.Users.Select(u => u.MatchState).OfType<TeamVersusUserState>().Count(), Is.EqualTo(2));

            // Both users must be split across teams, not placed in the same one.
            int[] teamIds = swapped.Users.Select(u => ((TeamVersusUserState)u.MatchState!).TeamID).ToArray();
            Assert.That(teamIds, Is.Unique);
            Assert.That(environment.RoomRepository.Rooms.First(r => r.Id == room.RoomID).Type, Is.EqualTo(OsuMatchType.TeamVersus));
        });
    }

    [Test]
    public async Task ChangeSettings_AsNonHost_ThrowsNotHost()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        Assert.That(
            async () => await guest.Hub.ChangeSettings(new MultiplayerRoomSettings { Name = "hacked" }),
            Throws.TypeOf<NotHostException>());
    }

    [Test]
    public async Task LeaveRoom_ClearsCurrentRoom_AndBroadcastsUserLeft()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);
        host.Recorder.Clear();

        await guest.Hub.LeaveRoom();

        Assert.Multiple(() =>
        {
            Assert.That(room.Users.Any(u => u.UserID == 2), Is.False);
            Assert.That(host.Recorder.UserLeftEvents, Has.Count.EqualTo(1));
            Assert.That(host.Recorder.UserLeftEvents[0].UserID, Is.EqualTo(2));
            Assert.That(environment.PlayerManager.GetPlayer<LazerPlayer>(2)?.State.CurrentRoom, Is.Null);
        });
    }

    [Test]
    public async Task LeaveRoom_LastUser_DisbandsRoom()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        // Host leaves first; guest becomes host.
        await host.Hub.LeaveRoom();
        Assert.That(environment.RoomManager.GetRoom(room.RoomID), Is.Not.Null);

        // Last user leaves; the room is destroyed.
        await guest.Hub.LeaveRoom();
        Assert.That(environment.RoomManager.GetRoom(room.RoomID), Is.Null);
        Assert.That(environment.RoomRepository.Rooms.First(r => r.Id == room.RoomID).EndsAt, Is.Not.Null);
    }

    [Test]
    public async Task AddPlaylistItem_GuestLimit_Throws()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        // In host-only mode a guest cannot add items at all.
        Assert.That(
            async () => await guest.Hub.AddPlaylistItem(CreatePlaylistItem(100, 2)),
            Throws.TypeOf<NotHostException>());

        // Switch to all-players mode: guests may enqueue at most
        // RealtimeConfig.GuestPlaylistLimit (3) items.
        await host.Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            Name = room.Settings.Name,
            MatchType = room.Settings.MatchType,
            QueueMode = QueueMode.AllPlayers,
        });

        for (int i = 0; i < RealtimeConfig.DefaultGuestPlaylistLimit; i++)
        {
            await guest.Hub.AddPlaylistItem(CreatePlaylistItem(100 + i, 2));
        }

        Assert.That(
            async () => await guest.Hub.AddPlaylistItem(CreatePlaylistItem(200, 2)),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task HostOnly_AfterGameplay_CurrentItemExpired_AndDuplicateAdded()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        host.Recorder.Clear();

        // Starting with no ready users is rejected (aligned with the reference).
        Assert.That(
            async () => await host.Hub.StartMatch(),
            Throws.TypeOf<InvalidStateException>());

        // Complete a match: the current item is expired and (in host-only mode)
        // duplicated so a non-expired item always remains.
        await host.Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        await host.Hub.ChangeState(MultiplayerUserState.Ready);
        await host.Hub.StartMatch();
        await host.Hub.ChangeState(MultiplayerUserState.Loaded);
        await host.Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        await host.Hub.ChangeState(MultiplayerUserState.FinishedPlay);

        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;
        Assert.Multiple(() =>
        {
            Assert.That(realRoom.Playlist, Has.Count.EqualTo(2));
            Assert.That(realRoom.Playlist[0].Expired, Is.True);
            Assert.That(realRoom.Playlist[0].PlayedAt, Is.Not.Null);
            Assert.That(realRoom.Playlist[1].Expired, Is.False);
            Assert.That(realRoom.Playlist[1].PlayedAt, Is.Null);

            // The current item advances to the non-expired duplicate.
            Assert.That(realRoom.Settings.PlaylistItemId, Is.EqualTo(realRoom.Playlist[1].ID));
            Assert.That(host.Recorder.PlaylistItemChangedEvents, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(host.Recorder.PlaylistItemAddedEvents, Has.Count.EqualTo(1));
            Assert.That(host.Recorder.SettingsChangedEvents, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(host.Recorder.SettingsChangedEvents.Last().PlaylistItemId, Is.EqualTo(realRoom.Playlist[1].ID));
        });
    }

    [Test]
    public async Task RemovePlaylistItem_OnlyNonExpiredItem_Throws()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));

        Assert.That(
            async () => await host.Hub.RemovePlaylistItem(room.Playlist[0].ID),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task RemovePlaylistItem_CurrentItem_RepointsPlaylistItemIdBeforeRemoval()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await host.Hub.AddPlaylistItem(CreatePlaylistItem(200, 1));
        host.Recorder.Clear();

        long currentItemId = room.Playlist[0].ID;
        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;

        await host.Hub.RemovePlaylistItem(currentItemId);

        Assert.Multiple(() =>
        {
            // The remaining item becomes the current item.
            Assert.That(realRoom.Settings.PlaylistItemId, Is.EqualTo(realRoom.Playlist.Single().ID));
            Assert.That(realRoom.Playlist.Single().ID, Is.Not.EqualTo(currentItemId));

            // SettingsChanged (with the repointed PlaylistItemId) must be
            // broadcast BEFORE the removal so the client's CurrentPlaylistItem
            // (Single()) never fails.
            int settingsIndex = host.Recorder.EventLog.FindIndex(e => string.Equals(e, nameof(IMultiplayerClient.SettingsChanged), StringComparison.Ordinal));
            int removalIndex = host.Recorder.EventLog.FindIndex(e => string.Equals(e, nameof(IMultiplayerClient.PlaylistItemRemoved), StringComparison.Ordinal));
            Assert.That(settingsIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(removalIndex, Is.GreaterThan(settingsIndex));
        });
    }

    [Test]
    public async Task EditPlaylistItem_CurrentItemBeatmapChanged_ResetsInvalidFreestyleStyles()
    {
        FakeBeatmapRepository beatmapRepository = new();
        MultiplayerHubTestEnvironment environment = new(beatmapRepository);
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        long itemId = room.Playlist[0].ID;

        // Make the current item freestyle (beatmap 100 belongs to set 1).
        await host.Hub.EditPlaylistItem(CreateFreestyleItem(itemId, 100));
        host.Recorder.Clear();
        guest.Recorder.Clear();

        // Guest selects a difficulty (100) which is valid for the current set.
        await guest.Hub.ChangeUserStyle(100, 0);
        Assert.That(room.Users.First(u => u.UserID == 2).BeatmapId, Is.EqualTo(100));

        // Editing the current item to a different beatmap set (200 belongs to set 2)
        // invalidates the guest's selection, which must be reset to null.
        await host.Hub.EditPlaylistItem(CreateFreestyleItem(itemId, 200));

        Assert.Multiple(() =>
        {
            // The guest's beatmap selection no longer belongs to the current set
            // and must be reset; the ruleset (osu!standard) remains valid.
            Assert.That(room.Users.First(u => u.UserID == 2).BeatmapId, Is.Null);
            Assert.That(guest.Recorder.UserStyleChangedEvents, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(guest.Recorder.UserStyleChangedEvents.Last(), Is.EqualTo((2, (int?)null, 0)));
        });
    }

    [Test]
    public async Task EditPlaylistItem_PreservesPlaylistOrder()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await host.Hub.AddPlaylistItem(CreatePlaylistItem(200, 1));

        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;
        long firstId = realRoom.Playlist[0].ID;
        ushort firstOrder = realRoom.Playlist[0].PlaylistOrder;
        ushort secondOrder = realRoom.Playlist[1].PlaylistOrder;

        // Editing the first item with a different order must not change it.
        await host.Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = firstId,
            OwnerID = 1,
            BeatmapID = 300,
            RulesetID = 0,
            RequiredMods = [],
            AllowedMods = [],
            Freestyle = false,
            Expired = false,
            PlaylistOrder = (ushort)(secondOrder + 1),
            WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
        });

        Assert.Multiple(() =>
        {
            Assert.That(realRoom.Playlist[0].PlaylistOrder, Is.EqualTo(firstOrder));
            Assert.That(realRoom.Playlist[1].PlaylistOrder, Is.EqualTo(secondOrder));
        });
    }

    [Test]
    public async Task AddPlaylistItem_AndEdit_ResolveStarRatingFromBeatmap()
    {
        FakeBeatmapRepository beatmapRepository = new();
        MultiplayerHubTestEnvironment environment = new(beatmapRepository);
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        IRoom realRoom = environment.RoomManager.GetRoom(room.RoomID)!;

        // Adding a playlist item for beatmap 200 resolves its star rating from
        // the beatmap repository (the created item has no beatmap navigation).
        await host.Hub.AddPlaylistItem(CreatePlaylistItem(200, 1));
        Assert.That(realRoom.Playlist.First(p => p.BeatmapID == 200).StarRating, Is.EqualTo(6.5));

        // Editing an item's beatmap re-resolves the star rating.
        long itemId = realRoom.Playlist.First(p => p.BeatmapID == 200).ID;
        await host.Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = itemId,
            OwnerID = 1,
            BeatmapID = 100,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            RequiredMods = [],
            AllowedMods = [],
            Freestyle = false,
            Expired = false,
            PlaylistOrder = 0,
            WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
        });

        Assert.That(realRoom.Playlist.First(p => p.ID == itemId).StarRating, Is.EqualTo(4.2));
    }

    [Test]
    public async Task AddPlaylistItem_WithInvalidMod_Throws()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));

        MultiplayerPlaylistItem item = CreatePlaylistItem(200, 1);
        item.RequiredMods = [new APIMod { Acronym = "VN" }];

        Assert.That(
            async () => await host.Hub.AddPlaylistItem(item),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task EditPlaylistItem_WithInvalidMod_Throws()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));

        MultiplayerPlaylistItem item = CreatePlaylistItem(200, 1);
        item.ID = room.Playlist[0].ID;
        item.RequiredMods = [new APIMod { Acronym = "VN" }];

        Assert.That(
            async () => await host.Hub.EditPlaylistItem(item),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task ChangeUserMods_WithModNotAllowedByCurrentItem_Throws()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        // The current item allows no free mods (AllowedMods is empty and the item
        // is not freestyle), so selecting HardRock must be rejected.
        Assert.That(
            async () => await guest.Hub.ChangeUserMods([new APIMod { Acronym = "HR" }]),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task ChangeUserMods_WithAllowedMod_Succeeds()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        // Make the current item allow HardRock.
        long itemId = room.Playlist[0].ID;
        MultiplayerPlaylistItem edit = CreatePlaylistItem(100, 1);
        edit.ID = itemId;
        APIMod[] hardRock = [new() { Acronym = "HR" }];
        edit.AllowedMods = hardRock;
        await host.Hub.EditPlaylistItem(edit);

        Assert.DoesNotThrowAsync(async () => await guest.Hub.ChangeUserMods(hardRock));

        string[] expectedAcronyms = ["HR"];
        Assert.That(room.Users.First(u => u.UserID == 2).Mods.Select(m => m.Acronym), Is.EqualTo(expectedAcronyms));
    }

    [Test]
    public async Task ChangeUserStyle_RulesetMismatchWithItem_Throws()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        // Make the current item's native ruleset taiko (1).
        long itemId = room.Playlist[0].ID;
        MultiplayerPlaylistItem edit = CreatePlaylistItem(100, 1);
        edit.ID = itemId;
        edit.RulesetID = 1;
        await host.Hub.EditPlaylistItem(edit);

        // A non-freestyle item rejects any user style selection (aligned with
        // the reference changeUserStyle).
        Assert.That(
            async () => await guest.Hub.ChangeUserStyle(beatmapId: null, 3),
            Throws.TypeOf<InvalidStateException>());
        Assert.That(
            async () => await guest.Hub.ChangeUserStyle(beatmapId: null, 1),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task ChangeUserStyle_FreestyleItemAllowsRulesetSelection()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");
        TestHubHandle guest = await environment.ConnectAsync(2, "conn-guest");

        MultiplayerRoom room = await host.Hub.CreateRoom(CreateRoomRequest(OsuMatchType.HeadToHead));
        await guest.Hub.JoinRoom(room.RoomID);

        // Make the current item freestyle.
        long itemId = room.Playlist[0].ID;
        MultiplayerPlaylistItem freestyle = CreateFreestyleItem(itemId, 100);
        await host.Hub.EditPlaylistItem(freestyle);

        // A freestyle item allows any ruleset selection.
        Assert.DoesNotThrowAsync(async () => await guest.Hub.ChangeUserStyle(beatmapId: null, 3));
        Assert.That(room.Users.First(u => u.UserID == 2).RulesetId, Is.EqualTo(3));
    }

    [Test]
    public async Task CreateRoom_WithInvalidMod_Throws()
    {
        MultiplayerHubTestEnvironment environment = new();
        TestHubHandle host = await environment.ConnectAsync(1, "conn-host");

        MultiplayerRoom request = CreateRoomRequest(OsuMatchType.HeadToHead);
        request.Playlist[0].RequiredMods = [new APIMod { Acronym = "VN" }];

        Assert.That(
            async () => await host.Hub.CreateRoom(request),
            Throws.TypeOf<InvalidStateException>());
    }

    private static MultiplayerPlaylistItem CreateFreestyleItem(long id, int beatmapId) => new()
    {
        ID = id,
        OwnerID = 1,
        BeatmapID = beatmapId,
        BeatmapChecksum = "checksum",
        RulesetID = 0,
        RequiredMods = [],
        AllowedMods = [],
        Freestyle = true,
        Expired = false,
        PlaylistOrder = 0,
        WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
    };

    private static MultiplayerPlaylistItem CreatePlaylistItem(int beatmapId, int ownerId) => new()
    {
        ID = 0,
        OwnerID = ownerId,
        BeatmapID = beatmapId,
        BeatmapChecksum = "checksum",
        RulesetID = 0,
        RequiredMods = [],
        AllowedMods = [],
        Freestyle = false,
        Expired = false,
        PlaylistOrder = 0,
        WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
    };

    private static MultiplayerRoom CreateRoomRequest(OsuMatchType type, string? password = null)
    {
        return new MultiplayerRoom(0)
        {
            Settings = new MultiplayerRoomSettings
            {
                Name = "test room",
                MatchType = type,
                Password = password ?? string.Empty,
                QueueMode = QueueMode.HostOnly,
                AutoStartDuration = TimeSpan.Zero,
            },
            Playlist = new List<MultiplayerPlaylistItem>
            {
                new()
                {
                    ID = 0,
                    OwnerID = 1,
                    BeatmapID = 100,
                    RulesetID = 0,
                    RequiredMods = [],
                    AllowedMods = [],
                    Freestyle = false,
                    Expired = false,
                    PlaylistOrder = 0,
                    WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
                },
            },
        };
    }

    private sealed class MultiplayerHubTestEnvironment
    {
        public MultiplayerHubTestEnvironment(IBeatmapRepository? beatmapRepository = null)
        {
            IpcClient = new InterProcessCommunicationClient(new RecordingTransport(), "realtime-tests");
            PlayerManager = new PlayerManager(NullLogger<PlayerManager>.Instance, IpcClient);
            RoomRepository = new FakeRoomRepository();
            Router = new MultiplayerClientRouter();

            ScopeFactory = new FakeScopeFactory(RoomRepository, beatmapRepository);
            RoomFacade = new RoomFacade(ScopeFactory, new MultiplayerEventDispatcher(
                new FakeHubContext(Router),
                NullLogger<MultiplayerEventDispatcher>.Instance), NullLogger<RoomFacade>.Instance)
            {
                RulesetManager = new RulesetManager(Path.GetTempPath()),
            };
            RoomManager = new RoomManager(NullLogger<RoomManager>.Instance, ScopeFactory, RoomFacade);
            HubContext = new FakeHubContext(Router);
        }

        public PlayerManager PlayerManager { get; }

        public FakeRoomRepository RoomRepository { get; }

        public RoomManager RoomManager { get; }

        public IRoomFacade RoomFacade { get; }

        public IServiceScopeFactory ScopeFactory { get; }

        public InterProcessCommunicationClient IpcClient { get; }

        public MultiplayerClientRouter Router { get; }

        public FakeHubContext HubContext { get; }

        public async Task<TestHubHandle> ConnectAsync(int userId, string connectionId)
        {
            Router.EnsureConnection(connectionId);
            Router.AliasConnection(userId.ToString(), connectionId);

            FakeGroupManager groupManager = new(Router);
            MultiplayerHub hub = new(
                PlayerManager,
                NullLogger<MultiplayerHub>.Instance,
                RoomManager,
                new MultiplayerEventDispatcher(new FakeHubContext(Router, groupManager), NullLogger<MultiplayerEventDispatcher>.Instance),
                ScopeFactory)
            {
                Context = new TestHubCallerContext(connectionId, userId.ToString()),
                Clients = new FakeHubCallerClients(Router, connectionId),
                Groups = groupManager,
            };

            await hub.OnConnectedAsync();

            TestHubHandle handle = new(hub, connectionId, Router.GetRecorder(userId.ToString()), groupManager);
            handle.Recorder.Clear();
            handle.GroupManager.Reset();
            return handle;
        }
    }

    private sealed record TestHubHandle(
        MultiplayerHub Hub,
        string ConnectionId,
        RecordingMultiplayerClient Recorder,
        FakeGroupManager GroupManager);

    /// <summary>
    /// In-memory <see cref="IBeatmapRepository"/> with a fixed set of difficulties.
    /// </summary>
    private sealed class FakeBeatmapRepository : IBeatmapRepository
    {
        private readonly List<Beatmap> _beatmaps =
        [
            new() { Id = 100, BeatmapSetId = 1, Mode = 0, Checksum = "checksum", DifficultyRating = 4.2 },
            new() { Id = 200, BeatmapSetId = 2, Mode = 0, Checksum = "checksum", DifficultyRating = 6.5 },
        ];

        public Task<Beatmap?> GetByIdAsync(int beatmapId)
            => Task.FromResult(_beatmaps.Find(b => b.Id == beatmapId));

        public Task<Beatmap?> GetByChecksumAsync(string checksum) => Task.FromResult<Beatmap?>(null);

        public Task<Beatmap> GetOrFetchByIdAsync(int beatmapId, g0v0.Server.Common.Fetching.IFetcher fetcher, CancellationToken cancellationToken = default)
            => Task.FromResult(_beatmaps.Find(b => b.Id == beatmapId) ?? new Beatmap { Id = beatmapId, BeatmapSetId = beatmapId, Mode = 0, Checksum = "checksum", DifficultyRating = 4.2 });

        public Task<Beatmap> GetOrFetchByChecksumAsync(string checksum, g0v0.Server.Common.Fetching.IFetcher fetcher, CancellationToken cancellationToken = default)
            => Task.FromResult(_beatmaps.Find(b => string.Equals(b.Checksum, checksum, StringComparison.Ordinal)) ?? new Beatmap { Id = 0, BeatmapSetId = 0, Mode = 0, Checksum = checksum, DifficultyRating = 4.2 });

        public Task<IReadOnlyList<Beatmap>> GetByBeatmapSetIdAsync(int beatmapSetId)
            => Task.FromResult<IReadOnlyList<Beatmap>>(_beatmaps.Where(b => b.BeatmapSetId == beatmapSetId).ToList());

        public Task<IReadOnlyList<Beatmap>> GetByMapperIdAsync(int mapperId)
            => Task.FromResult<IReadOnlyList<Beatmap>>([]);

        public Task CreateAsync(Beatmap beatmap) => Task.CompletedTask;

        public Task UpdateAsync(Beatmap beatmap) => Task.CompletedTask;

        public Task DeleteAsync(Beatmap beatmap) => Task.CompletedTask;
    }
}