// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for the host-only playlist queue mode, ported from osu-server-spectator's
/// <c>MultiplayerHostOnlyQueueTests</c>.
/// </summary>
[TestFixture]
public class MultiplayerHostOnlyQueueTests : MultiplayerTestBase
{
    [Test]
    public async Task GuestsCannotAddItems()
    {
        await Hub.JoinRoom(ROOMID);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);

        // In host-only queue mode a non-host cannot add playlist items.
        Assert.That(
            async () => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
            }),
            Throws.TypeOf<NotHostException>());
    }

    [Test]
    public async Task HostMayAddManyItems()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });

        for (int i = 1; i < RealtimeConfig.DefaultGuestPlaylistLimit + 1; i++)
        {
            await addItem();
        }

        async Task addItem() => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });
    }

    [Test]
    public async Task RoomHasNewPlaylistItemAfterMatchStartWithOneItem()
    {
        // SettingsChanged broadcasts pass the live room settings reference (which
        // is mutated in place as the playlist advances), so capture the broadcast
        // playlist item ID at the time each broadcast occurs.
        var settingsPlaylistItemIds = new List<long>();
        var playlistItemChangedSnapshots = new List<(long Id, bool Expired)>();
        Receiver.Setup(r => r.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()))
                .Callback<MultiplayerRoomSettings>(s => settingsPlaylistItemIds.Add(s.PlaylistItemId))
                .CallBase();
        Receiver.Setup(r => r.PlaylistItemChanged(It.IsAny<MultiplayerPlaylistItem>()))
                .Callback<MultiplayerPlaylistItem>(i => playlistItemChangedSnapshots.Add((i.ID, i.Expired)))
                .CallBase();

        await Hub.JoinRoom(ROOMID);

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.Playlist, Has.Count.EqualTo(2));

        var oldItem = room.Playlist[0];
        var newItem = room.Playlist[1];

        // Room has new playlist item.
        Assert.That(newItem.ID, Is.Not.EqualTo(oldItem.ID));
        Assert.That(newItem.Expired, Is.False);
        Assert.That(oldItem.Expired, Is.True);
        Assert.That(room.Settings.PlaylistItemId, Is.EqualTo(newItem.ID));

        // Players received callbacks. The reference expects two SettingsChanged
        // broadcasts here (one from room initialisation and one from match
        // completion); g0v0 assigns the initial PlaylistItemId in the room
        // constructor without broadcasting, so only the completion broadcast occurs.
        Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == newItem.ID)), Times.Once);
        Assert.That(playlistItemChangedSnapshots, Does.Contain((oldItem.ID, true)));
        Assert.That(settingsPlaylistItemIds, Is.EqualTo(new[] { newItem.ID }));

        // And a second time...
        await Hub.ChangeState(MultiplayerUserState.Idle);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.Playlist, Has.Count.EqualTo(3));

        oldItem = room.Playlist[1];
        newItem = room.Playlist[2];

        // Room has new playlist item.
        Assert.That(newItem.ID, Is.Not.EqualTo(oldItem.ID));
        Assert.That(newItem.Expired, Is.False);
        Assert.That(oldItem.Expired, Is.True);
        Assert.That(room.Settings.PlaylistItemId, Is.EqualTo(newItem.ID));

        // Players received callbacks (one SettingsChanged per completed match).
        Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == newItem.ID)), Times.Once);
        Assert.That(playlistItemChangedSnapshots, Does.Contain((oldItem.ID, true)));
        Assert.That(settingsPlaylistItemIds, Is.EqualTo(new[] { room.Playlist[1].ID, room.Playlist[2].ID }));
    }

    [Test]
    public async Task NewPlaylistNotAddedAfterMatchStartWithMultipleItems()
    {
        // SettingsChanged broadcasts pass the live room settings reference (which
        // is mutated in place as the playlist advances), so capture the broadcast
        // playlist item ID at the time each broadcast occurs.
        var settingsPlaylistItemIds = new List<long>();
        var playlistItemChangedSnapshots = new List<(long Id, bool Expired)>();
        Receiver.Setup(r => r.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()))
                .Callback<MultiplayerRoomSettings>(s => settingsPlaylistItemIds.Add(s.PlaylistItemId))
                .CallBase();
        Receiver.Setup(r => r.PlaylistItemChanged(It.IsAny<MultiplayerPlaylistItem>()))
                .Callback<MultiplayerPlaylistItem>(i => playlistItemChangedSnapshots.Add((i.ID, i.Expired)))
                .CallBase();

        // Add another item in free-for-all mode.
        await Hub.JoinRoom(ROOMID);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        // Play the first item in host-only mode.
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.Playlist, Has.Count.EqualTo(2));

        Assert.That(room.Playlist[1].ID, Is.EqualTo(room.Settings.PlaylistItemId));
        Assert.That(room.Playlist[0].Expired, Is.True);

        // The two queue-mode switches each broadcast SettingsChanged, and match
        // completion adds a third when the current item advances. The reference
        // counts one fewer because its room initialisation emits an extra
        // broadcast that g0v0 does not.
        Receiver.Verify(r => r.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Once);
        Assert.That(playlistItemChangedSnapshots, Does.Contain((room.Playlist[0].ID, true)));
        Assert.That(settingsPlaylistItemIds, Is.EqualTo(new[] { room.Playlist[0].ID, room.Playlist[0].ID, room.Playlist[1].ID }));

        // Play the second item in host-only mode.
        await Hub.ChangeState(MultiplayerUserState.Idle);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);

        room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        // The second item is the last non-expired item, so completing it (in
        // host-only mode) duplicates it and a new playlist item appears.
        Assert.That(room!.Playlist, Has.Count.EqualTo(3));
        Assert.That(room.Playlist[2].ID, Is.EqualTo(room.Settings.PlaylistItemId));
        Assert.That(room.Playlist[1].Expired, Is.True);

        // Players received callbacks (one SettingsChanged per completed match).
        Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == room.Playlist[2].ID)), Times.Once);
        Assert.That(playlistItemChangedSnapshots, Does.Contain((room.Playlist[1].ID, true)));
        Assert.That(settingsPlaylistItemIds, Is.EqualTo(new[] { room.Playlist[0].ID, room.Playlist[0].ID, room.Playlist[1].ID, room.Playlist[2].ID }));
    }

    [Test]
    public async Task ItemLastInQueueWhenChangingToAllPlayersMode()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);

        Assert.That(room!.Playlist, Has.Count.EqualTo(2));

        var firstItem = room.Playlist[0];
        var secondItem = room.Playlist[1];

        // First item (played).
        Assert.That(firstItem, Is.Not.Null);
        Assert.That(firstItem.Expired, Is.True);

        // Second item (previously current) is the current item.
        Assert.That(secondItem, Is.Not.Null);
        Assert.That(secondItem.Expired, Is.False);
        Assert.That(secondItem.ID, Is.EqualTo(room.Settings.PlaylistItemId));
    }
}