// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Extensions;
using NUnit.Framework;
using osu.Game.Online.Spectator;

namespace g0v0.Server.Realtime.Tests.Extensions;

[TestFixture]
public class SpectatorStateExtensionsTests
{
    [Test]
    public void IsPlaying_WhenStateIsPlaying_ShouldReturnTrue()
    {
        var state = new SpectatorState
        {
            State = SpectatedUserState.Playing,
        };

        Assert.That(state.IsPlaying(), Is.True);
    }

    [Test]
    public void IsPlaying_WhenStateIsFailed_ShouldReturnTrue()
    {
        var state = new SpectatorState
        {
            State = SpectatedUserState.Failed,
        };

        Assert.That(state.IsPlaying(), Is.True);
    }

    [Test]
    public void IsPlaying_WhenStateIsIdle_ShouldReturnFalse()
    {
        var state = new SpectatorState
        {
            State = SpectatedUserState.Idle,
        };

        Assert.That(state.IsPlaying(), Is.False);
    }
}