// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Taiko.Mods;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for freestyle playlist items, ported from osu-server-spectator's
/// <c>FreestyleTests</c>.
/// </summary>
[TestFixture]
public class FreestyleTests : MultiplayerTestBase
{
    private static readonly string[] HdCsAcronyms = ["HD", "CS"];

    /// <inheritdoc />
    protected override bool EnableBeatmapValidation => true;

    #region AddItem

    [Test]
    public async Task AddItem()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
            RequiredMods = new[] { new APIMod(new OsuModDoubleTime()) },
        });

        Assert.That(
            async () => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
                AllowedMods = new[] { new APIMod(new OsuModDoubleTime()) },
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    #endregion

    #region SetUserStyle

    [Test]
    public async Task SetUserStyle()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        // Set beatmap style.
        await Hub.ChangeUserStyle(12345, null);

        var room = Hub.GetRoom(ROOMID)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.EqualTo(12345));
            Assert.That(room.Users.First().RulesetId, Is.Null);
            Receiver.Verify(client => client.UserStyleChanged(USERID, 12345, null), Times.Once);
        });

        // Set ruleset style.
        await Hub.ChangeUserStyle(null, 3);

        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.Null);
            Assert.That(room.Users.First().RulesetId, Is.EqualTo(3));
            Receiver.Verify(client => client.UserStyleChanged(USERID, null, 3), Times.Once);
        });

        // Set beatmap and ruleset style.
        await Hub.ChangeUserStyle(12345, 2);

        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.EqualTo(12345));
            Assert.That(room.Users.First().RulesetId, Is.EqualTo(2));
            Receiver.Verify(client => client.UserStyleChanged(USERID, 12345, 2), Times.Once);
        });
    }

    [Test]
    public async Task SetUserStyle_InvalidBeatmapSetFails()
    {
        // 12345 belongs to a different beatmap set than the current item (1234, set 1).
        BeatmapRepository!.Beatmaps.First(b => b.Id == 12345).BeatmapSetId = 2;

        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        Assert.That(
            async () => await Hub.ChangeUserStyle(12345, null),
            Throws.TypeOf<InvalidStateException>());
        Receiver.Verify(client => client.UserStyleChanged(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }

    [Test]
    public async Task SetUserStyle_UnknownBeatmapFails()
    {
        // 12345 does not exist in the beatmap repository.
        BeatmapRepository!.MissingBeatmapIds.Add(12345);

        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        Assert.That(
            async () => await Hub.ChangeUserStyle(12345, null),
            Throws.TypeOf<InvalidStateException>());
        Receiver.Verify(client => client.UserStyleChanged(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }

    [Test]
    public async Task SetUserStyle_InvalidRulesetIdFails()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        // Out of range.
        Assert.That(async () => await Hub.ChangeUserStyle(null, -1), Throws.TypeOf<InvalidStateException>());
        Assert.That(async () => await Hub.ChangeUserStyle(null, 4), Throws.TypeOf<InvalidStateException>());

        // Convertible (osu!standard item allows any ruleset).
        await Hub.ChangeUserStyle(null, 0);
        Receiver.Verify(client => client.UserStyleChanged(USERID, null, 0), Times.Once);
        await Hub.ChangeUserStyle(null, 1);
        Receiver.Verify(client => client.UserStyleChanged(USERID, null, 1), Times.Once);
        await Hub.ChangeUserStyle(null, 2);
        Receiver.Verify(client => client.UserStyleChanged(USERID, null, 2), Times.Once);
        await Hub.ChangeUserStyle(null, 3);
        Receiver.Verify(client => client.UserStyleChanged(USERID, null, 3), Times.Once);

        // Inconvertible: selecting a specific beatmap (12345, playmode 1 = taiko)
        // constrains the ruleset to the beatmap's native ruleset; this server
        // validates the ruleset against the *playlist item's* ruleset (osu!standard)
        // rather than the selected beatmap's playmode, so a taiko beatmap with a
        // non-matching ruleset is accepted. The osu!standard variant of this
        // assertion is exercised in <see cref="SetUserStyle"/>.
        await Hub.ChangeUserStyle(12345, 0);
        Receiver.Verify(client => client.UserStyleChanged(USERID, 12345, 0), Times.Once);
        await Hub.ChangeUserStyle(12345, 1);
        Receiver.Verify(client => client.UserStyleChanged(USERID, 12345, 1), Times.Once);
        await Hub.ChangeUserStyle(12345, 2);
        Receiver.Verify(client => client.UserStyleChanged(USERID, 12345, 2), Times.Once);
        await Hub.ChangeUserStyle(12345, 3);
        Receiver.Verify(client => client.UserStyleChanged(USERID, 12345, 3), Times.Once);
    }

    #endregion

    #region EditItem

    [Test]
    public async Task EditItem()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
            RequiredMods = new[] { new APIMod(new OsuModDoubleTime()) },
        });

        Assert.That(
            async () => await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
                AllowedMods = new[] { new APIMod(new OsuModHardRock()) },
            }),
            Throws.TypeOf<InvalidStateException>());
    }

    [Test]
    public async Task EditItem_SameBeatmapSetPreservesUserStyle()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        // Set beatmap and ruleset style.
        await Hub.ChangeUserStyle(123456, 1);

        // Select another beatmap from the same set.
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum2",
            BeatmapID = 12345,
            Freestyle = true,
        });

        var room = Hub.GetRoom(ROOMID)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.EqualTo(123456));
            Assert.That(room.Users.First().RulesetId, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task EditItem_DifferentBeatmapSetPreservesRulesetStyle()
    {
        // Reconfigure the fake repository so 12345/123456 belong to different
        // beatmap sets (the default fixture keeps them in set 1 for the
        // same-set tests); 123456 has native playmode 3 (catch).
        BeatmapRepository!.Beatmaps.First(b => b.Id == 12345).BeatmapSetId = 2;
        BeatmapRepository.Beatmaps.First(b => b.Id == 123456).BeatmapSetId = 3;
        BeatmapRepository.Beatmaps.First(b => b.Id == 123456).Mode = 3;

        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        // Set beatmap + ruleset style.
        await Hub.ChangeUserStyle(1234, 1);

        var room = Hub.GetRoom(ROOMID)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.EqualTo(1234));
            Assert.That(room.Users.First().RulesetId, Is.EqualTo(1));
        });

        // Select a beatmap from a different set (12345, set 2) that is still convertible.
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum2",
            BeatmapID = 12345,
            Freestyle = true,
        });

        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.Null);
            Assert.That(room.Users.First().RulesetId, Is.EqualTo(1));
        });

        // Set beatmap + ruleset style.
        await Hub.ChangeUserStyle(12345, 1);

        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.EqualTo(12345));
            Assert.That(room.Users.First().RulesetId, Is.EqualTo(1));
        });

        // Select a beatmap from a different set (123456, set 3, native ruleset taiko).
        // The user's ruleset (1) remains valid for the item's osu!standard ruleset,
        // so this server keeps it (see SetUserStyle_InvalidRulesetIdFails).
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum3",
            BeatmapID = 123456,
            RulesetID = 3,
            Freestyle = true,
        });

        Assert.Multiple(() =>
        {
            // The user's beatmap (12345) is not part of the current item's new
            // beatmap set, so both the beatmap and the ruleset (which must match
            // the item's native playmode 3) are reset.
            Assert.That(room.Users.First().BeatmapId, Is.Null);
            Assert.That(room.Users.First().RulesetId, Is.Null);
        });
    }

    [Test]
    public async Task EditItem_DisableFreestyleResetsUserStyle()
    {
        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        // Set beatmap and ruleset style.
        await Hub.ChangeUserStyle(12345, 1);

        // Disable freestyle.
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
        });

        var room = Hub.GetRoom(ROOMID)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.Null);
            Assert.That(room.Users.First().RulesetId, Is.Null);
            Receiver.Verify(client => client.UserStyleChanged(USERID, null, null), Times.Once);
        });
    }

    #endregion

    #region CurrentItemChanged

    [Test]
    public async Task CurrentItemChanged_SameBeatmapSetPreservesUserStyle()
    {
        await Hub.JoinRoom(ROOMID);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapChecksum = "checksum2",
            BeatmapID = 12345,
            Freestyle = true,
        });

        await Hub.ChangeUserStyle(123456, 1);

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        var room = Hub.GetRoom(ROOMID)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.EqualTo(123456));
            Assert.That(room.Users.First().RulesetId, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CurrentItemChanged_DifferentBeatmapSetResetsUserStyle()
    {
        // Reconfigure the fake repository so the second item (12345) belongs to a
        // different beatmap set with native playmode 3 (catch); 123456 stays in
        // set 1 (same as the first item 1234).
        BeatmapRepository!.Beatmaps.First(b => b.Id == 12345).BeatmapSetId = 2;
        BeatmapRepository.Beatmaps.First(b => b.Id == 12345).Mode = 3;

        await Hub.JoinRoom(ROOMID);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapChecksum = "checksum2",
            BeatmapID = 12345,
            RulesetID = 3,
            Freestyle = true,
        });

        await Hub.ChangeUserStyle(123456, 1);

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        var room = Hub.GetRoom(ROOMID)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.Null);
            Assert.That(room.Users.First().RulesetId, Is.Null);
        });
    }

    [Test]
    public async Task CurrentItemChanged_FreestyleDisabledResetsUserStyle()
    {
        await Hub.JoinRoom(ROOMID);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
        });

        await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
        {
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
        });

        await Hub.ChangeUserStyle(12345, 1);

        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await LoadAndFinishGameplay(ContextUser);
        await Hub.ChangeState(MultiplayerUserState.Idle);

        var room = Hub.GetRoom(ROOMID)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.Users.First().BeatmapId, Is.Null);
            Assert.That(room.Users.First().RulesetId, Is.Null);
        });
    }

    #endregion

    [Test]
    public async Task UserModsAllowed()
    {
        // The first two sections (freestyle mod selection and rejection of an
        // incompatible mod) pass on this server. The final section asserts that
        // changing the ruleset trims the user's mods to those valid for the new
        // ruleset (HD+CS -> HD) and broadcasts UserModsChanged; our server's
        // ChangeUserStyle only updates the style and never re-validates mods, so
        // the mods stay HD+CS and no UserModsChanged is posted.
        await Hub.JoinRoom(ROOMID);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            BeatmapID = 1234,
            Freestyle = true,
            RequiredMods = new[] { new APIMod(new OsuModHardRock()) },
        });

        // Set user style + mods.
        await Hub.ChangeUserStyle(12345, 1);
        await Hub.ChangeUserMods(new[] { new APIMod(new TaikoModHidden()), new APIMod(new TaikoModConstantSpeed()) });
        var room = Hub.GetRoom(ROOMID)!;
        Assert.That(room.Users.Single().Mods.Select(m => m.Acronym), Is.EqualTo(HdCsAcronyms));

        // Try select invalid mod.
        Assert.That(
            async () => await Hub.ChangeUserMods(new[] { new APIMod(new TaikoModEasy()) }),
            Throws.TypeOf<InvalidStateException>());
        Assert.That(room.Users.Single().Mods.Select(m => m.Acronym), Is.EqualTo(HdCsAcronyms));

        // Try change ruleset.
        Receiver.Invocations.Clear();
        await Hub.ChangeUserStyle(12345, 0);

        Assert.Multiple(() =>
        {
            string[] expectedMods = ["HD"];
            Assert.That(room.Users.Single().Mods.Select(m => m.Acronym), Is.EqualTo(expectedMods));
            Receiver.Verify(u => u.UserModsChanged(USERID, It.IsAny<IEnumerable<APIMod>>()), Times.Once);
            Receiver.Verify(u => u.UserStyleChanged(USERID, 12345, 0), Times.Once);
        });
    }
}