// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using NUnit.Framework;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Tests.Objects.States;

[TestFixture]
public class PlayerStateTests
{
    [Test]
    public void ToLazerUserPresence_WhenStatusIsNull_ReturnsNull()
    {
        var state = new PlayerState(new IdleActivity())
        {
            UserStatus = null,
        };

        Assert.That(state.ToLazerUserPresence(), Is.Null);
    }

    [Test]
    public void ToLazerUserPresence_WhenStatusIsOffline_ReturnsNull()
    {
        var state = new PlayerState(new IdleActivity())
        {
            UserStatus = UserStatus.Offline,
        };

        Assert.That(state.ToLazerUserPresence(), Is.Null);
    }

    [Test]
    public void ToLazerUserPresence_WhenStatusIsVisible_ReturnsConvertedPresence()
    {
        var state = new PlayerState(new SoloActivity
        {
            BeatmapId = 123,
            BeatmapDisplayTitle = "Song Title",
            RulesetId = 4,
            RulesetPlayingVerb = "Playing",
        })
        {
            UserStatus = UserStatus.Online,
        };

        UserPresence? presence = state.ToLazerUserPresence();

        Assert.That(presence, Is.Not.Null);
        UserPresence value = presence!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(value.Status, Is.EqualTo(UserStatus.Online));
            Assert.That(value.Activity, Is.TypeOf<UserActivity.InSoloGame>());
        });

        var activity = (UserActivity.InSoloGame)value.Activity!;
        Assert.Multiple(() =>
        {
            Assert.That(activity.BeatmapID, Is.EqualTo(123));
            Assert.That(activity.BeatmapDisplayTitle, Is.EqualTo("Song Title"));
            Assert.That(activity.RulesetID, Is.EqualTo(4));
            Assert.That(activity.RulesetPlayingVerb, Is.EqualTo("Playing"));
        });
    }
}
