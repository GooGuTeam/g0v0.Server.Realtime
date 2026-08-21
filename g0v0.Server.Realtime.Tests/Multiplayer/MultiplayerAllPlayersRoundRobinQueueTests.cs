// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// The reference's round-robin queue documentation uses comment blocks with
// blank-line separation (queue/owner tables), which SA1512 rejects.
#pragma warning disable SA1512

using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for the all-players round-robin playlist queue mode, ported from
/// osu-server-spectator's <c>MultiplayerAllPlayersRoundRobinQueueTests</c>.
/// </summary>
[TestFixture]
public class MultiplayerAllPlayersRoundRobinQueueTests : MultiplayerTestBase
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RoundRobinTestWithEditItemDuringGameplay(bool endWithAbort)
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        CheckCurrentItem(1);

        await AddItem();
        await AddItem();

        CheckCurrentItem(1);
        CheckOrder(1, 2, 3);

        SetUserContext(ContextUser);
        await AddItem();

        CheckCurrentItem(1);
        CheckOrder(1, 2, 4, 3);

        // user 1: 1 4
        // user 2: 2 3
        //
        // queue: 1, 2, 4, 3

        await RunGameplay();
        CheckCurrentItem(2);
        CheckOrder(2, 4, 3);

        // user 1: 4
        // user 2: 2 3
        //
        // queue: 2, 4, 3

        await RunGameplay();
        CheckCurrentItem(4);
        CheckOrder(4, 3);

        // user 1: 4
        // user 2: 3
        //
        // queue: 4, 3

        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(ContextUser2);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await Hub.StartMatch();
        await LoadGameplay(ContextUser, ContextUser2);

        CheckCurrentItem(4);
        CheckOrder(4, 3);

        // user 1: 4
        // user 2: 3
        //
        // queue: 4, 3

        // This prepares the breakage. the OwnerID of the playlist item changes but the item's order is retained.
        // must be host that performs this (only they have permission to edit other user's items).
        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        MultiplayerPlaylistItem item3 = room!.Playlist.Single(p => p.ID == 3);

        SetUserContext(ContextUser);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem(new PlaylistItem(item3)));

        // required to complete the breakage (runs updatePlaylistOrder).
        await AddItem();

        // user 1: 3, 4, 5
        // user 2:
        //
        // queue: 3 (interleave by user, order by playlist_item_id, OH NOOOO)

        CheckCurrentItem(4);
        CheckOrder(4, 3, 5);

        if (endWithAbort)
        {
            await Hub.AbortMatch();
        }
        else
        {
            await FinishGameplay(ContextUser, ContextUser2);

            await Hub.ChangeState(MultiplayerUserState.Idle);

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Idle);
        }

        CheckCurrentItem(3);
        CheckOrder(3, 5);
    }

    [Test]
    public async Task RoundRobinOrderingWithGameplay()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        CheckCurrentItem(1);

        // User 1 adds an extra item
        await AddItem();

        CheckCurrentItem(1);
        CheckOrder(1, 2);

        // Item played.
        await RunGameplay();
        CheckCurrentItem(2);
        CheckOrder(2);

        // User 2 adds two items.
        SetUserContext(ContextUser2);
        await AddItem();
        await AddItem();

        CheckCurrentItem(2);
        CheckOrder(2, 3, 4);

        // User 1 adds an item.
        SetUserContext(ContextUser);
        await AddItem();

        CheckCurrentItem(2);
        CheckOrder(2, 3, 5, 4);

        // Gameplay is now run to ensure the ordering doesn't change.
        await RunGameplay();
        CheckCurrentItem(3);
        await RunGameplay();
        CheckCurrentItem(5);
        await RunGameplay();
        CheckCurrentItem(4);

        // After playing the last item, it remains as the current item.
        await RunGameplay();
        CheckCurrentItem(4);
    }

    [Test]
    public async Task RoundRobinOrderingWithManyUsers()
    {
        CreateUser(1, out Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>? contextUser1, out _);
        CreateUser(2, out Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>? contextUser2, out _);
        CreateUser(3, out Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>? contextUser3, out _);
        CreateUser(4, out Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>? contextUser4, out _);

        // ---
        // User 1 joins and begins adding items.
        // ---

        SetUserContext(contextUser1);
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   | (new)
        CheckOrder(1);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  2   |  1   | (new)
        await AddItem();
        CheckOrder(1, 2);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  2   |  1   |
        // |  3   |  1   | (new)
        await AddItem();
        CheckOrder(1, 2, 3);

        // ---
        // User 2 joins and begins adding items.
        // ---

        SetUserContext(contextUser2);
        await Hub.JoinRoom(RoomId);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   | (new)
        // |  2   |  1   |
        // |  3   |  1   |
        await AddItem();
        CheckOrder(1, 4, 2, 3);

        // ---
        // User 3 joins and begins adding items.
        // ---

        SetUserContext(contextUser3);
        await Hub.JoinRoom(RoomId);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   |
        // |  5   |  3   | (new)
        // |  2   |  1   |
        // |  3   |  1   |
        await AddItem();
        CheckOrder(1, 4, 5, 2, 3);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   |
        // |  5   |  3   |
        // |  2   |  1   |
        // |  6   |  3   | (new)
        // |  3   |  1   |
        await AddItem();
        CheckOrder(1, 4, 5, 2, 6, 3);

        // ---
        // User 2 adds more items.
        // ---

        SetUserContext(contextUser2);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   |
        // |  5   |  3   |
        // |  2   |  1   |
        // |  7   |  2   | (new)
        // |  6   |  3   |
        // |  3   |  1   |
        await AddItem();
        CheckOrder(1, 4, 5, 2, 7, 6, 3);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   |
        // |  5   |  3   |
        // |  2   |  1   |
        // |  7   |  2   |
        // |  6   |  3   |
        // |  3   |  1   |
        // |  8   |  2   | (new)
        await AddItem();
        CheckOrder(1, 4, 5, 2, 7, 6, 3, 8);

        // ---
        // User 4 joins and begins adding items.
        // ---

        SetUserContext(contextUser4);
        await Hub.JoinRoom(RoomId);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   |
        // |  5   |  3   |
        // |  9   |  4   | (new)
        // |  2   |  1   |
        // |  7   |  2   |
        // |  6   |  3   |
        // |  3   |  1   |
        // |  8   |  2   |
        await AddItem();
        CheckOrder(1, 4, 5, 9, 2, 7, 6, 3, 8);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   |
        // |  5   |  3   |
        // |  9   |  4   |
        // |  2   |  1   |
        // |  7   |  2   |
        // |  6   |  3   |
        // |  10  |  4   | (new)
        // |  3   |  1   |
        // |  8   |  2   |
        await AddItem();
        CheckOrder(1, 4, 5, 9, 2, 7, 6, 10, 3, 8);

        // | item | user |
        // | ---- | ---- |
        // |  1   |  1   |
        // |  4   |  2   |
        // |  5   |  3   |
        // |  9   |  4   |
        // |  2   |  1   |
        // |  7   |  2   |
        // |  6   |  3   |
        // |  10  |  4   |
        // |  3   |  1   |
        // |  8   |  2   |
        // |  11  |  4   | (new)
        await AddItem();
        CheckOrder(1, 4, 5, 9, 2, 7, 6, 10, 3, 8, 11);
    }

    [Test]
    public async Task OrderUpdatedOnRemoval()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });
        await AddItem();
        await AddItem();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await AddItem();
        await AddItem();
        await AddItem();

        // Items should be initially interleaved.
        CheckOrder(1, 4, 2, 5, 3, 6);

        // Remove item with id 4.
        await Hub.RemovePlaylistItem(4);
        CheckOrder(1, 5, 2, 6, 3);

        // Remove item with id 5.
        await Hub.RemovePlaylistItem(5);
        CheckOrder(1, 6, 2, 3);
    }

    [Test]
    public async Task RemoveWhenCurrentItemIsAtEndOfList()
    {
        CreateUser(1, out Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>? user1Ctx, out _);
        CreateUser(2, out Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>? user2Ctx, out _);

        SetUserContext(user1Ctx);
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

        // Queue: [ 1 ]
        // List: [ 1 ]

        // Add two more items.
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem { BeatmapID = 2, BeatmapChecksum = "checksum" });
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem { BeatmapID = 3, BeatmapChecksum = "checksum" });

        // Queue: [ 1, 2, 3 ]
        // List: [ 1, 2, 3 ]

        // Join another user and add one more item.
        SetUserContext(user2Ctx);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem { BeatmapID = 4, BeatmapChecksum = "checksum" });

        // Queue: [ 1, 4, 2, 3 ]
        // List: [ 1, 2, 3, 4 ]

        // Run gameplay.
        SetUserContext(user1Ctx);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(user2Ctx);
        await MarkCurrentUserReadyAndAvailable();
        SetUserContext(user1Ctx);

        await Hub.StartMatch();
        await LoadAndFinishGameplay(user1Ctx, user2Ctx);
        await Hub.ChangeState(MultiplayerUserState.Idle);
        SetUserContext(user2Ctx);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        // Queue: [ 4, 2, 3 ]
        // List: [ 1, 2, 3, 4 ]

        // Now we'll remove item 2.
        // Notice that while item 4 is at the front of the queue, it is also at the _end_ of the playlist.
        // Item 3 will have its playlist order updated, which may crash if the current item isn't updated to not reference beyond the end of the list.
        SetUserContext(user1Ctx);
        await Hub.RemovePlaylistItem(2);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.CurrentItem.ID, Is.EqualTo(4));
    }

    private async Task RunGameplay()
    {
        SetUserContext(ContextUser2);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        await LoadAndFinishGameplay(ContextUser, ContextUser2);

        SetUserContext(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        SetUserContext(ContextUser);
    }

    private async Task AddItem()
    {
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });
    }

    private void CheckCurrentItem(long expectedItemId)
    {
        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.PlaylistItemId, Is.EqualTo(expectedItemId));
    }

    private void CheckOrder(params long[] itemIdsInOrder)
    {
        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(
            room!.Playlist.Where(item => !item.Expired).OrderBy(item => item.PlaylistOrder).Select(item => item.ID),
            Is.EqualTo(itemIdsInOrder));
    }
}