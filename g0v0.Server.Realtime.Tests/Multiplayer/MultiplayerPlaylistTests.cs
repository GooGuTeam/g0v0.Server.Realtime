// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

#pragma warning disable SA1117

using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for playlist management behaviour, ported from osu-server-spectator's
/// <c>MultiplayerPlaylistTests</c> and adapted to this server's in-memory
/// room/playlist model (no database layer).
/// </summary>
[TestFixture]
public class MultiplayerPlaylistTests : MultiplayerTestBase
{
    /// <inheritdoc />
    protected override bool EnableBeatmapValidation => true;

    [Test]
    public async Task AddNonExistentBeatmap()
    {
        // Beatmap 3333 is not registered in the beatmap repository.
        BeatmapRepository!.MissingBeatmapIds.Add(3333);

        await Hub.JoinRoom(RoomId);

        Assert.That(
            async () => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "checksum",
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task AddCustomizedBeatmapThrows()
    {
        await Hub.JoinRoom(RoomId);

        // The supplied checksum does not match the beatmap's checksum in the database.
        Assert.That(
            async () => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 1234,
                BeatmapChecksum = "modified-checksum",
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [TestCase(ILegacyRuleset.MAX_LEGACY_RULESET_ID + 1)]
    [TestCase(-1)]
    [TestCase(99)]
    public async Task AddCustomRulesetThrows(int rulesetId)
    {
        await Hub.JoinRoom(RoomId);

        Assert.That(
            async () => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = rulesetId,
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task AddCustomRulesetAcceptsAllAvailableRulesets()
    {
        await Hub.JoinRoom(RoomId);

        foreach (int rulesetId in BeatmapRepository != null
                     ? [0, 1, 2, 3]
                     : Array.Empty<int>())
        {
            Assert.DoesNotThrowAsync(async () => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = rulesetId,
            }));
        }
    }

    [Test]
    public async Task AddingItemWithRulesetIncompatibleWithBeatmapThrows()
    {
        await Hub.JoinRoom(RoomId);

        // Beatmap 100 has native playmode 1 (osu!taiko); selecting ruleset 2 is invalid.
        Assert.That(
            async () => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 100,
                BeatmapChecksum = "checksum",
                RulesetID = 2,
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task AddingItemWithRulesetCompatibleWithBeatmapSucceeds(int scoreRulesetId)
    {
        await Hub.JoinRoom(RoomId);

        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 9999,
            BeatmapChecksum = "checksum",
            RulesetID = scoreRulesetId,
        });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(2));
        Assert.That(room!.Playlist.First(i => i.BeatmapID == 9999).RulesetID, Is.EqualTo(scoreRulesetId));
    }

    [Test]
    public async Task RoomStartsWithCurrentPlaylistItem()
    {
        await Hub.JoinRoom(RoomId);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.PlaylistItemId, Is.EqualTo(1));
    }

    [Test]
    public async Task RoomStartsWithCorrectQueueingMode()
    {
        // Pre-seed the room in the repository with the AllPlayers queue mode so
        // joining loads it from the database layer, mirroring the reference test's
        // pre-configured database room.
        await SeedRoomInRepository(QueueMode.AllPlayers);

        await Hub.JoinRoomExplicit(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Settings.QueueMode, Is.EqualTo(QueueMode.AllPlayers));
        Assert.That(room!.Playlist, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task JoinedRoomContainsAllPlaylistItems()
    {
        // Pre-seed the room in the repository with two playlist items so joining
        // loads both, mirroring the reference test's pre-populated database room.
        await SeedRoomInRepository(QueueMode.HostOnly, addExtraItem: true);

        await Hub.JoinRoomExplicit(RoomId);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(2));
        Assert.That(room!.Playlist[0].BeatmapID, Is.EqualTo(1234));
        Assert.That(room!.Playlist[1].BeatmapID, Is.EqualTo(3333));
    }

    [Test]
    public async Task GuestsCanRemoveTheirOwnItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        await Hub.RemovePlaylistItem(2);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(1));
        Receiver.Verify(client => client.PlaylistItemRemoved(2), Times.Once);
    }

    [Test]
    public async Task GuestsCanNotRemoveOtherUsersItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        Assert.That(
            async () => await Hub.RemovePlaylistItem(2),
            Throws.TypeOf<InvalidStateException>());
        Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task HostCanRemoveGuestsItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        SetUserContext(ContextUser);
        await Hub.RemovePlaylistItem(2);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(1));
        Receiver.Verify(client => client.PlaylistItemRemoved(2), Times.Once);
    }

    [Test]
    public async Task ExternalItemsCanNotBeRemoved()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        Assert.That(
            async () => await Hub.RemovePlaylistItem(3),
            Throws.TypeOf<InvalidStateException>());
        Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task CurrentItemCanNotBeRemovedIfSingle()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        Assert.That(
            async () => await Hub.RemovePlaylistItem(1),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task CurrentItemCanBeRemovedIfNotSingle()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        await Hub.RemovePlaylistItem(1);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(1));
        Assert.That(room!.Settings.PlaylistItemId, Is.EqualTo(2));
        Receiver.Verify(client => client.PlaylistItemRemoved(1), Times.Once);
    }

    [Test]
    public async Task ExpiredItemsCanNotBeRemoved()
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
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        Assert.That(
            async () => await Hub.RemovePlaylistItem(1),
            Throws.TypeOf<InvalidStateException>());
        Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task GuestsCanUpdateTheirOwnItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 2,
            BeatmapID = 4444,
            BeatmapChecksum = "4444",
        });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(2));
        Receiver.Verify(client => client.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Once);
        Receiver.Verify(client => client.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == 2 && i.BeatmapID == 4444)), Times.Once);
    }

    [Test]
    public async Task EditingItemWithRulesetIncompatibleWithBeatmapThrows()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        // Beatmap 100 has native playmode 1; selecting ruleset 2 is invalid.
        Assert.That(
            async () => await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 2,
                BeatmapID = 100,
                BeatmapChecksum = "checksum",
                RulesetID = 2,
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task EditingItemWithRulesetCompatibleWithBeatmapSucceeds(int scoreRulesetId)
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 2,
            BeatmapID = 4444,
            BeatmapChecksum = "4444",
            RulesetID = scoreRulesetId,
        });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist.First(i => i.ID == 2).RulesetID, Is.EqualTo(scoreRulesetId));
    }

    [Test]
    public async Task HostCanUpdateGuestsItems()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        SetUserContext(ContextUser);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 2,
            BeatmapID = 4444,
            BeatmapChecksum = "4444",
        });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(2));
        Receiver.Verify(client => client.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Once);
        Receiver.Verify(client => client.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == 2 && i.BeatmapID == 4444)), Times.Once);
    }

    [Test]
    public async Task HostCanChangeItemsWhenMaxItemsReached()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        for (int i = 1; i < RealtimeConfig.DefaultGuestPlaylistLimit; i++)
        {
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
            });
        }

        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapID = 4444,
            BeatmapChecksum = "4444",
        });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Playlist, Has.Count.EqualTo(RealtimeConfig.DefaultGuestPlaylistLimit));
        Assert.That(room!.Playlist[0].BeatmapID, Is.EqualTo(4444));
    }

    [Test]
    public async Task ExpiredItemsCanNotBeChanged()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        Assert.That(
            async () => await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task PlaylistItemsWithScoreTokenInDatabaseCannotBeRemoved()
    {
        await Hub.JoinRoom(RoomId);

        Realtime.Objects.Rooms.IRoom room = Hub.GetRoom(RoomId)!;
        long itemId = room.Playlist.Single().ID;

        // Simulate an existing score token for the item.
        RoomRepository.ScoreTokenPlaylistItemIds.Add(itemId);

        Assert.That(
            async () => await Hub.RemovePlaylistItem(itemId),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task CurrentlyPlayingItemsCannotBeChangedOrRemoved()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        Assert.That(
            async () => await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
            }),
            Throws.TypeOf<InvalidStateException>());

        Assert.That(
            async () => await Hub.RemovePlaylistItem(1),
            Throws.TypeOf<InvalidStateException>());

        await LoadGameplay(ContextUser);

        Assert.That(
            async () => await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
            }),
            Throws.TypeOf<InvalidStateException>());

        Assert.That(
            async () => await Hub.RemovePlaylistItem(1),
            Throws.TypeOf<InvalidStateException>());

        await FinishGameplay(ContextUser);
    }

    [Test]
    public async Task OwnerChangesWhenItemChanges()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        // The guest owns the added item.
        Realtime.Objects.Rooms.IRoom room = Hub.GetRoom(RoomId)!;
        Assert.That(room.Playlist.Single(i => i.BeatmapID == 3333).OwnerID, Is.EqualTo(UserId2));

        // The host edits the item; ownership transfers to the host.
        SetUserContext(ContextUser);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = room.Playlist.Single(i => i.BeatmapID == 3333).ID,
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        Assert.That(room.Playlist.Single(i => i.BeatmapID == 3333).OwnerID, Is.EqualTo(UserId));
    }

    [Test]
    public async Task PlayersCanNotReadyWithAllItemsExpired()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        // All items are expired; readying up must fail.
        Realtime.Objects.Rooms.IRoom room = Hub.GetRoom(RoomId)!;
        Assert.That(room.Playlist.All(i => i.Expired), Is.True);

        Assert.That(
            async () => await Hub.ChangeState(MultiplayerUserState.Ready),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task PlayersUnReadiedWhenCurrentItemIsEdited()
    {
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapID = 3333,
            BeatmapChecksum = "3333",
        });

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users[1].State, Is.EqualTo(MultiplayerUserState.Idle));
    }

    [Test]
    public async Task SettingsChangedNotifiedBeforeItemIsRemoved()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapID = 1,
            BeatmapChecksum = "checksum",
        });

        int currentSequenceId = 0;
        int removedAtSequenceId = 0;
        int settingsChangedAtSequenceId = 0;

        Receiver.Setup(d => d.PlaylistItemRemoved(It.IsAny<long>()))
                .Callback(() => removedAtSequenceId = ++currentSequenceId).CallBase();
        Receiver.Setup(d => d.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()))
                .Callback(() => settingsChangedAtSequenceId = ++currentSequenceId).CallBase();

        await Hub.RemovePlaylistItem(1);

        Assert.That(settingsChangedAtSequenceId, Is.EqualTo(1));
        Assert.That(removedAtSequenceId, Is.EqualTo(2));
    }

    /// <summary>
    /// Pre-seeds a room with ID <see cref="MultiplayerTestBase.RoomId"/> in the
    /// fake room repository so a subsequent explicit join loads it from the
    /// database layer, mirroring the reference tests' pre-configured rooms.
    /// </summary>
    /// <param name="queueMode">The queue mode to store on the room.</param>
    /// <param name="addExtraItem">Whether to seed a second playlist item.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SeedRoomInRepository(QueueMode queueMode, bool addExtraItem = false)
    {
        List<MultiplayerPlaylistItem> playlist =
        [

            new()
            {
                OwnerID = UserId,
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                PlaylistOrder = 0,
            }
        ];

        if (addExtraItem)
        {
            playlist.Add(new MultiplayerPlaylistItem
            {
                OwnerID = UserId,
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
                RulesetID = 0,
                PlaylistOrder = 1,
            });
        }

        await RoomRepository.CreateRoom(
            new MultiplayerRoom(RoomId)
            {
                Settings = new MultiplayerRoomSettings
                {
                    Name = "test room",
                    MatchType = OsuMatchType.HeadToHead,
                    QueueMode = queueMode,
                    Password = string.Empty,
                },
                Playlist = playlist,
            },
            UserId);
    }
}