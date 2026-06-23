// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Objects.States.Activity;
using NUnit.Framework;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Tests.Extensions;

[TestFixture]
public class LazerUserActivityExtensionTests
{
    [Test]
    public void ToRealtimeActivity_WhenActivityIsNull_ReturnsIdleActivity()
    {
        IUserActivity activity = LazerUserActivityExtension.ToRealtimeActivity(null);

        Assert.That(activity, Is.TypeOf<IdleActivity>());
    }

    [Test]
    public void ToRealtimeActivity_WhenSpectatingUserTitleIsNull_UsesEmptyString()
    {
        var activity = new UserActivity.SpectatingUser
        {
            ScoreID = 42,
            PlayerName = "peppy",
            BeatmapID = 321,
            BeatmapDisplayTitle = null,
        };

        SpectatingUserActivity result = (SpectatingUserActivity)activity.ToRealtimeActivity();

        Assert.Multiple(() =>
        {
            Assert.That(result.ScoreID, Is.EqualTo(42));
            Assert.That(result.PlayerName, Is.EqualTo("peppy"));
            Assert.That(result.BeatmapID, Is.EqualTo(321));
            Assert.That(result.BeatmapDisplayTitle, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void ToRealtimeActivity_WhenSoloGame_PreservesGameplayProperties()
    {
        var activity = new UserActivity.InSoloGame
        {
            BeatmapID = 123,
            BeatmapDisplayTitle = "Song Title",
            RulesetID = 7,
            RulesetPlayingVerb = "Playing",
        };

        SoloActivity result = (SoloActivity)activity.ToRealtimeActivity();

        Assert.Multiple(() =>
        {
            Assert.That(result.BeatmapId, Is.EqualTo(123));
            Assert.That(result.BeatmapDisplayTitle, Is.EqualTo("Song Title"));
            Assert.That(result.RulesetId, Is.EqualTo(7));
            Assert.That(result.RulesetPlayingVerb, Is.EqualTo("Playing"));
        });
    }
}