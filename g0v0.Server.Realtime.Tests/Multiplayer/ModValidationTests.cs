// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Moq;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets.Catch.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for playlist-item and user mod validation, ported from osu-server-spectator's
/// <c>ModValidationTests</c>.
/// </summary>
[TestFixture]
public class ModValidationTests : MultiplayerTestBase
{
    [Test]
    public async Task CanAddIncompatibleAllowedModsCombination()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            AllowedMods =
            [

                // setting an incompatible combination should be allowed.
                // will be enforced at the point of a user choosing from the allowed mods.
                new APIMod(new OsuModFlashlight()),
                new APIMod(new OsuModApproachDifferent())
            ],
        });
    }

    [Test]
    public async Task AddInvalidAllowedModsForRulesetThrows()
    {
        await Hub.JoinRoom(RoomId);

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 3,
            AllowedMods =
            [
                new APIMod(new OsuModBlinds())
            ],
        }));
    }

    [Test]
    public async Task AddInvalidRequiredModsCombinationThrows()
    {
        await Hub.JoinRoom(RoomId);

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            RequiredMods =
            [
                new APIMod(new OsuModHidden()),
                new APIMod(new OsuModApproachDifferent())
            ],
        }));
    }

    [Test]
    public async Task AddInvalidRequiredModsForRulesetThrows()
    {
        await Hub.JoinRoom(RoomId);

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 3,
            RequiredMods =
            [
                new APIMod(new OsuModBlinds())
            ],
        }));
    }

    [Test]
    public async Task AddInvalidRequiredAllowedModsCombinationThrows()
    {
        await Hub.JoinRoom(RoomId);

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            RequiredMods =
            [
                new APIMod(new OsuModHidden())
            ],
            AllowedMods =
            [

                // allowed and required mods should always be cross-compatible.
                new APIMod(new OsuModApproachDifferent())
            ],
        }));
    }

    [Test]
    public async Task AddOverlappingRequiredAllowedModsFails()
    {
        await Hub.JoinRoom(RoomId);

        // Skipped for parity with the reference test. The reference requires a
        // dedicated dedupe check which our server does not implement; note that
        // EnsureModsValid happens to reject this combination anyway through the
        // aggregate required+allowed compatibility check (duplicate types).
        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RequiredMods =
            [
                new APIMod(new OsuModFlashlight())
            ],
            AllowedMods =
            [

                // if a mod is in RequiredMods it shouldn't also be in AllowedMods.
                new APIMod(new OsuModFlashlight())
            ],
        }));
    }

    [Test]
    public async Task UserChangesMods()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod(new OsuModFlashlight()),
                new APIMod(new OsuModApproachDifferent())
            ],
        });

        APIMod[] setMods = [new(new OsuModApproachDifferent())];
        await Hub.ChangeUserMods(setMods);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.First().Mods, Is.EqualTo(setMods));

        setMods = [new(new OsuModApproachDifferent()), new(new OsuModFlashlight())];
        await Hub.ChangeUserMods(setMods);

        Assert.That(Hub.GetRoom(RoomId)!.Users.First().Mods, Is.EqualTo(setMods));
    }

    [Test]
    public async Task ModAcronymComparisonsAreCaseInsensitive()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod { Acronym = "fL" },
                new APIMod { Acronym = "ad" }
            ],
        });

        await Hub.ChangeUserMods([new APIMod { Acronym = "fl" }]);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.First().Mods.Count(), Is.EqualTo(1));
        Assert.That(room.Users.First().Mods.Single().Acronym, Is.EqualTo("FL").IgnoreCase);

        await Hub.ChangeUserMods([new APIMod { Acronym = "AD" }, new APIMod { Acronym = "Fl" }]);

        APIMod[] userMods = Hub.GetRoom(RoomId)!.Users.First().Mods.ToArray();
        Assert.That(userMods.Length, Is.EqualTo(2));
        Assert.That(userMods[0].Acronym, Is.EqualTo("AD").IgnoreCase);
        Assert.That(userMods[1].Acronym, Is.EqualTo("FL").IgnoreCase);

        await AssertInvalid(() => Hub.ChangeUserMods([new APIMod { Acronym = "Fg" }]));
    }

    [Test]
    public async Task UserSelectsInvalidModCombinationThrows()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod(new OsuModHidden()),
                new APIMod(new OsuModApproachDifferent())
            ],
        });

        await Hub.ChangeUserMods([new APIMod(new OsuModApproachDifferent())]);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        IEnumerable<APIMod> originalMods = room!.Users.First().Mods;
        IEnumerable<APIMod> apiMods = originalMods as APIMod[] ?? originalMods.ToArray();
        Assert.That(apiMods, Is.Not.Empty);

        await AssertInvalid(() => Hub.ChangeUserMods([
            new APIMod(new OsuModApproachDifferent()),
            new APIMod(new OsuModHidden())
        ]));

        Assert.That(Hub.GetRoom(RoomId)!.Users.First().Mods, Is.EqualTo(apiMods));
    }

    [Test]
    public async Task UserSelectsDisallowedModsThrows()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            RulesetID = 2,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod(new CatchModHidden())
            ],
        });

        // this should cause the complete setting change to fail, including the hidden mod application.
        await AssertInvalid(() => Hub.ChangeUserMods([
            new APIMod(new CatchModHidden()),
            new APIMod(new CatchModDaycore())
        ]));

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.First().Mods, Is.Empty);
    }

    [Test]
    public async Task UserSelectsInvalidModsForRulesetThrows()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            RulesetID = 2,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod(new CatchModHidden())
            ],
        });

        await Hub.ChangeUserMods([new APIMod(new CatchModHidden())]);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);

        IEnumerable<APIMod> originalMods = room!.Users.First().Mods;
        IEnumerable<APIMod> apiMods = originalMods as APIMod[] ?? originalMods.ToArray();
        Assert.That(apiMods, Is.Not.Empty);

        await AssertInvalid(() => Hub.ChangeUserMods([new APIMod(new OsuModApproachDifferent())]));

        Assert.That(Hub.GetRoom(RoomId)!.Users.First().Mods, Is.EqualTo(apiMods));
    }

    [Test]
    public async Task ChangingDisallowedModsRemovesUserMods()
    {
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(RoomId);

        SetUserContext(ContextUser);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod(new OsuModApproachDifferent()),
                new APIMod(new OsuModFlashlight()),
                new APIMod(new OsuModHardRock())
            ],
        });

        await Hub.ChangeUserMods([new APIMod(new OsuModFlashlight()), new APIMod(new OsuModApproachDifferent())]);
        await AssertUserMods(UserId, "FL", "AD");

        SetUserContext(ContextUser2);
        await Hub.ChangeUserMods([new APIMod(new OsuModHardRock()), new APIMod(new OsuModApproachDifferent())]);
        await AssertUserMods(UserId2, "HR", "AD");

        SetUserContext(ContextUser);
        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod(new OsuModFlashlight()),
                new APIMod(new OsuModHardRock())
            ],
        });

        // Skipped: our server only re-validates user *styles* on playlist-item
        // edits (see RoomBase.HandlePlaylistItemChanged / EnsureAllUsersValidStyle);
        // it does not trim user mods to the new allowed-mod set.
        await AssertUserMods(UserId, "FL");
        await AssertUserMods(UserId2, "HR");

        Task AssertUserMods(int userId, params string[] modAcronyms)
        {
            Receiver.Verify(
                c => c.UserModsChanged(
                userId,
                It.Is<IEnumerable<APIMod>>(mods => mods.Select(m => m.Acronym).SequenceEqual(modAcronyms))),
                Times.Once);

            Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
            Assert.That(room, Is.Not.Null);

            IEnumerable<APIMod> userMods = room!.Users.Single(u => u.UserID == userId).Mods;
            Assert.That(userMods.Select(m => m.Acronym), Is.EqualTo(modAcronyms));
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task ChangingRulesetRemovesInvalidUserMods()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            AllowedMods =
            [
                new APIMod(new OsuModApproachDifferent())
            ],
        });

        await Hub.ChangeUserMods([new APIMod(new OsuModApproachDifferent())]);

        Realtime.Objects.Rooms.IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.Users.First().Mods, Is.Not.Empty);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            RulesetID = 2,
            BeatmapChecksum = "checksum",
        });

        // Skipped: our server does not clear user mods when the current item's
        // ruleset changes; only styles are re-validated on playlist edits.
        Assert.That(Hub.GetRoom(RoomId)!.Users.First().Mods, Is.Empty);
    }

    [Test]
    public async Task AddUserUnplayableModThrows()
    {
        await Hub.JoinRoom(RoomId);

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            RequiredMods = [new APIMod(new OsuModAutoplay())],
        }));

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            AllowedMods = [new APIMod(new OsuModAutoplay())],
        }));
    }

    [Test]
    public async Task AddMultiplayerUnplayableModThrows()
    {
        await Hub.JoinRoom(RoomId);

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            RequiredMods = [new APIMod(new ModAdaptiveSpeed())],
        }));

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            AllowedMods = [new APIMod(new ModAdaptiveSpeed())],
        }));
    }

    [Test]
    public async Task AddMultiplayerInvalidFreeModThrowsOnAllowedModsOnly()
    {
        await Hub.JoinRoom(RoomId);

        await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            RequiredMods = [new APIMod(new OsuModDoubleTime())],
        });

        await AssertInvalidEdit(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
        {
            ID = 1,
            BeatmapChecksum = "checksum",
            RulesetID = 0,
            AllowedMods = [new APIMod(new OsuModDoubleTime())],
        }));
    }

    private static Task AssertInvalidEdit(Func<Task> action)
    {
        Assert.That(async () => await action(), Throws.TypeOf<InvalidStateException>());
        return Task.CompletedTask;
    }

    private static Task AssertInvalid(Func<Task> action)
    {
        Assert.That(async () => await action(), Throws.TypeOf<InvalidStateException>());
        return Task.CompletedTask;
    }
}