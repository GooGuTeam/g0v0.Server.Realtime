// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for the all-players playlist queue mode, ported from osu-server-spectator's
/// <c>MultiplayerAllPlayersQueueTests</c>.
/// </summary>
[TestFixture]
public class MultiplayerAllPlayersQueueTests : MultiplayerTestBase
{
    [Test]
    public async Task AddingItemAppendsToQueue()
    {
        long playlistItemId = (await Hub.JoinRoom(RoomId)).Settings.PlaylistItemId;
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        MultiplayerPlaylistItem newItem = new()
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        };

        await Hub.AddPlaylistItem(newItem);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        // The currently-selected playlist item is unchanged by the addition.
        Assert.That(room!.Settings.PlaylistItemId, Is.EqualTo(playlistItemId));

        // The added item is broadcast to the room with its server-assigned ID
        // (unlike the reference, which mutates and broadcasts the caller's object).
        Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == room.Playlist[1].ID && i.BeatmapID == 3333)), Times.Once);

        // Exactly one settings change: the queue-mode switch. The reference expects
        // two because its room initialisation broadcasts SettingsChanged when the
        // playlist item ID is first assigned; g0v0 instead initialises the ID in the
        // room constructor without broadcasting.
        Receiver.Verify(r => r.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()), Times.Once);
    }

    [Test]
    public async Task CompletingItemExpiresAndDoesNotAddNewItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        // The room maintains its single playlist item.
        Assert.That(room!.Playlist, Has.Count.EqualTo(1));

        // Room maintains playlist item.
        Assert.That(room.Playlist[0].ID, Is.EqualTo(room.Settings.PlaylistItemId));
        Assert.That(room.Playlist[0].Expired, Is.True);

        // Players received callbacks.
        Receiver.Verify(r => r.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Never);
        Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == room.Playlist[0].ID && i.Expired)), Times.Once);
        Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
    }

    [Test]
    public async Task NewItemImmediatelySelectedWhenAllItemsExpired()
    {
        // Skipped for parity with the reference. The reference always repoints
        // Settings.PlaylistItemId to the first non-expired item after adding
        // (StandardMatchController.AddPlaylistItem -> updateCurrentItem), so a
        // newly added item is immediately selected when all items are expired.
        // g0v0's RoomBase.AddPlaylistItemInternal only calls UpdateCurrentItem
        // when PlaylistItemId == 0 (the very first item), so the expired item
        // stays selected here.
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        MultiplayerPlaylistItem newItem = new()
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        };

        await Hub.AddPlaylistItem(newItem);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        MultiplayerPlaylistItem secondItem = room!.Playlist[1];

        // New playlist item selected.
        Assert.That(secondItem.ID, Is.EqualTo(room.Settings.PlaylistItemId));
        Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
    }

    [Test]
    public async Task ItemsNotClearedWhenChangingToHostOnlyMode()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 4444,
            BeatmapChecksum = "4444",
        });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        // All items still present in the room.
        Assert.That(room!.Playlist, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task OneNonExpiredItemExistsWhenChangingToHostOnlyMode()
    {
        // Skipped for parity with the reference. The reference duplicates the
        // current (expired) playlist item when switching to host-only mode while
        // every item is expired, guaranteeing a non-expired item remains
        // (StandardMatchController.updateQueueFromModeChange). g0v0's
        // RoomBase.ChangeRoomSettings only implements that duplication in
        // HandleGameplayCompleted (on gameplay completion), not on mode change,
        // so the room keeps a single expired item here.
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.Playlist, Has.Count.EqualTo(2));

        MultiplayerPlaylistItem firstItem = room.Playlist[0];
        MultiplayerPlaylistItem secondItem = room.Playlist[1];

        // First item (played).
        Assert.That(firstItem.ID, Is.Not.EqualTo(room.Settings.PlaylistItemId));
        Assert.That(firstItem.Expired, Is.True);

        // Second item (new).
        Assert.That(secondItem.ID, Is.EqualTo(room.Settings.PlaylistItemId));
        Assert.That(secondItem.Expired, Is.False);
    }

    [Test]
    public async Task HostMayAddManyItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        for (int i = 1; i < RealtimeConfig.DefaultGuestPlaylistLimit; i++)
        {
            await AddItem();
        }

        // Host should be allowed to add many items even in non-host-only queue modes.
        await AddItem();
        await AddItem();
        await AddItem();

        async Task AddItem() => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });
    }

    [Test]
    public async Task UserMayOnlyAddLimitedNumberOfItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        // Play initial beatmap to clear the queue.
        await PlayBeatmap();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        // Non-host user should be able to add items up to a defined limit.
        for (int i = 0; i < RealtimeConfig.DefaultGuestPlaylistLimit; i++)
        {
            await AddItem();
        }

        // User is not allowed to add more items.
        Assert.That(async () => await AddItem(), Throws.TypeOf<InvalidStateException>());

        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

        SetUserContext(ContextUser);

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser, ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        await AddItem();
        Assert.That(async () => await AddItem(), Throws.TypeOf<InvalidStateException>());

        async Task AddItem() => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        async Task PlayBeatmap()
        {
            SetUserContext(ContextUser);

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);
        }
    }
}