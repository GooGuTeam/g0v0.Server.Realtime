// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections;
using System.Reflection;
using g0v0.Server.Realtime.Objects.Rooms;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for countdown behaviour, ported from osu-server-spectator's
/// <c>CountdownTest</c>.
/// </summary>
[TestFixture]
public class CountdownTest : MultiplayerTestBase
{
    [Test]
    public async Task StartAndStopCountdown()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
        countdown.TimeRemaining = TimeSpan.FromMinutes(1);

        await room.StartCountdown(countdown, () => Task.CompletedTask);
        Assert.That(room.FindCountdownById(countdown.ID), Is.Not.Null);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown)), Times.Once);

        await room.StopCountdown(countdown);
        Assert.That(room.FindCountdownById(countdown.ID), Is.Null);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown.ID)), Times.Once);
    }

    [Test]
    public async Task CallbackInvokedWhenCountdownCompletes()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
        countdown.TimeRemaining = TimeSpan.FromMilliseconds(50);

        bool callbackInvoked = false;

        await room.StartCountdown(countdown, () =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        });

        await GetCountdownTask(room, countdown);

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public async Task CallbackInvokedWhenCountdownSkippedToEnd()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
        countdown.TimeRemaining = TimeSpan.FromMinutes(1);

        bool callbackInvoked = false;

        await room.StartCountdown(countdown, () =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        });

        await SkipToEndOfCountdown(room, countdown);

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public async Task CallbackNotInvokedWhenCountdownStopped()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
        countdown.TimeRemaining = TimeSpan.FromMinutes(1);

        bool callbackInvoked = false;

        await room.StartCountdown(countdown, () =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        });

        Task task = GetCountdownTask(room, countdown);

        await room.StopCountdown(countdown);
        await task;

        Assert.That(callbackInvoked, Is.False);
    }

    [Test]
    public async Task StartMultipleCountdowns()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
        MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();
        countdown1.TimeRemaining = TimeSpan.FromMinutes(1);
        countdown2.TimeRemaining = TimeSpan.FromMinutes(1);

        await room.StartCountdown(countdown1, () => Task.CompletedTask);
        await room.StartCountdown(countdown2, () => Task.CompletedTask);
        Assert.That(room.FindCountdownById(countdown1.ID), Is.Not.Null);
        Assert.That(room.FindCountdownById(countdown2.ID), Is.Not.Null);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown2)), Times.Once);
        Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownStoppedEvent>()), Times.Never);
    }

    [Test]
    public async Task CallbackInvokedWhenMultipleCountdownsComplete()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
        MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();
        countdown1.TimeRemaining = TimeSpan.FromMilliseconds(50);
        countdown2.TimeRemaining = TimeSpan.FromMilliseconds(150);

        bool callbackInvoked1 = false;
        bool callbackInvoked2 = false;

        await room.StartCountdown(countdown1, () =>
        {
            callbackInvoked1 = true;
            return Task.CompletedTask;
        });

        await room.StartCountdown(countdown2, () =>
        {
            callbackInvoked2 = true;
            return Task.CompletedTask;
        });

        Task task1 = GetCountdownTask(room, countdown1);
        Task task2 = GetCountdownTask(room, countdown2);

        // g0v0's RoomBase.RunCountdown completes countdowns without taking the
        // room lock (unlike the reference server), so countdown completions are
        // staggered to avoid two background tasks mutating ActiveCountdowns at
        // the same instant.
        await task1;
        await task2;

        Assert.That(callbackInvoked1, Is.True);
        Assert.That(callbackInvoked2, Is.True);
    }

    [Test]
    public async Task CallbackInvokedWhenMultipleCountdownsSkippedToEnd()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
        MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();
        countdown1.TimeRemaining = TimeSpan.FromMinutes(1);
        countdown2.TimeRemaining = TimeSpan.FromMinutes(1);

        bool callbackInvoked1 = false;
        bool callbackInvoked2 = false;

        await room.StartCountdown(countdown1, () =>
        {
            callbackInvoked1 = true;
            return Task.CompletedTask;
        });

        await room.StartCountdown(countdown2, () =>
        {
            callbackInvoked2 = true;
            return Task.CompletedTask;
        });

        Task task1 = SkipToEndOfCountdown(room, countdown1);

        // g0v0's RoomBase.RunCountdown completes countdowns without taking the
        // room lock (unlike the reference server), so the countdowns are skipped
        // and awaited one at a time to avoid two background tasks mutating
        // ActiveCountdowns at the same instant.
        await task1;

        Task task2 = SkipToEndOfCountdown(room, countdown2);
        await task2;

        Assert.That(callbackInvoked1, Is.True);
        Assert.That(callbackInvoked2, Is.True);
    }

    [Test]
    public async Task CallbackNotInvokedWhenMultipleCountdownsStopped()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
        MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();
        countdown1.TimeRemaining = TimeSpan.FromMinutes(1);
        countdown2.TimeRemaining = TimeSpan.FromMinutes(1);

        bool callbackInvoked1 = false;
        bool callbackInvoked2 = false;

        await room.StartCountdown(countdown1, () =>
        {
            callbackInvoked1 = true;
            return Task.CompletedTask;
        });

        await room.StartCountdown(countdown2, () =>
        {
            callbackInvoked2 = true;
            return Task.CompletedTask;
        });

        // g0v0's RoomBase.RunCountdown completes countdowns without taking the
        // room lock (unlike the reference server), so stopping countdown1 fully
        // (cancelling its stop token) before any other countdown completes is
        // guaranteed to suppress its callback. Countdown2 is then skipped to the
        // end, which runs its callback.
        Task task1 = GetCountdownTask(room, countdown1);

        await room.StopCountdown(countdown1);
        await task1;

        Task task2 = SkipToEndOfCountdown(room, countdown2);
        await task2;

        Assert.That(callbackInvoked1, Is.False);
        Assert.That(callbackInvoked2, Is.True);
    }

    [Test]
    public async Task StartingNewExclusiveCountdownReplacesExisting()
    {
        await Hub.JoinRoom(RoomId);

        RoomBase room = (RoomBase)Hub.GetRoom(RoomId)!;
        Mock<ExclusiveCountdown> countdown1 = new() { CallBase = true };
        Mock<MultiplayerCountdown> countdown2 = new() { CallBase = true };
        Mock<ExclusiveCountdown> countdown3 = new() { CallBase = true };
        countdown1.Object.TimeRemaining = TimeSpan.FromMinutes(1);
        countdown2.Object.TimeRemaining = TimeSpan.FromMinutes(1);
        countdown3.Object.TimeRemaining = TimeSpan.FromMinutes(1);

        await room.StartCountdown(countdown1.Object, () => Task.CompletedTask);
        await room.StartCountdown(countdown2.Object, () => Task.CompletedTask);
        await room.StartCountdown(countdown3.Object, () => Task.CompletedTask);

        Assert.That(room.FindCountdownById(countdown1.Object.ID), Is.Null);
        Assert.That(room.FindCountdownById(countdown2.Object.ID), Is.Not.Null);
        Assert.That(room.FindCountdownById(countdown3.Object.ID), Is.Not.Null);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown1.Object)), Times.Once);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown2.Object)), Times.Once);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown3.Object)), Times.Once);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown1.Object.ID)), Times.Once);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown2.Object.ID)), Times.Never);
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown3.Object.ID)), Times.Never);
    }

    [Test]
    public async Task MatchStartCountdownStartsMatchWhenCompleted()
    {
        await Hub.JoinRoom(RoomId);
        await MarkCurrentUserReadyAndAvailable();

        await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMilliseconds(50) });

        await Task.Delay(300);

        IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown is MatchStartCountdown)), Times.Once);
        Receiver.Verify(r => r.LoadRequested(), Times.Once);
    }

    [Test]
    public async Task AutoStartCountdownStartsWhenUserReady()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            AutoStartDuration = TimeSpan.FromMinutes(1),
        });

        // No countdown yet — no user is ready.
        IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.ActiveCountdowns.OfType<MatchStartCountdown>().Any(), Is.False);

        await MarkCurrentUserReadyAndAvailable();

        MatchStartCountdown countdown = room.ActiveCountdowns.OfType<MatchStartCountdown>().Single();
        Assert.That(countdown.TimeRemaining, Is.EqualTo(TimeSpan.FromMinutes(1)));
        Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown)), Times.Once);
    }

    [Test]
    public async Task AutoStartCountdownStartsMatchWhenCompleted()
    {
        await Hub.JoinRoom(RoomId);
        await Hub.ChangeSettings(new MultiplayerRoomSettings
        {
            AutoStartDuration = TimeSpan.FromMilliseconds(50),
        });

        await MarkCurrentUserReadyAndAvailable();

        await Task.Delay(300);

        IRoom? room = Hub.GetRoom(RoomId);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));
        Receiver.Verify(r => r.LoadRequested(), Times.Once);
    }

    /// <summary>
    /// Retrieves the task backing the given countdown, mirroring the reference
    /// implementation's <c>GetCountdownTask</c> which g0v0's <see cref="RoomBase"/>
    /// does not expose publicly.
    /// </summary>
    /// <param name="room">The room hosting the countdown.</param>
    /// <param name="countdown">The countdown to retrieve the task of.</param>
    /// <returns>The task which completes when the countdown finishes.</returns>
    private static Task GetCountdownTask(RoomBase room, MultiplayerCountdown countdown)
    {
        object countdownInfo = FindCountdownInfo(room, countdown);
        return (Task)countdownInfo.GetType().GetProperty("Task")!.GetValue(countdownInfo)!;
    }

    /// <summary>
    /// Skips to the end of the given countdown and runs its callback as soon as
    /// possible unless the countdown has been cancelled, mirroring the reference
    /// implementation's <c>SkipToEndOfCountdown</c>.
    /// </summary>
    /// <param name="room">The room hosting the countdown.</param>
    /// <param name="countdown">The countdown to skip.</param>
    /// <returns>A task which becomes completed when the active countdown completes.</returns>
    private static Task SkipToEndOfCountdown(RoomBase room, MultiplayerCountdown countdown)
    {
        object countdownInfo = FindCountdownInfo(room, countdown);
        CancellationTokenSource skipSource = (CancellationTokenSource)countdownInfo.GetType().GetProperty("SkipSource")!.GetValue(countdownInfo)!;
        skipSource.Cancel();
        return (Task)countdownInfo.GetType().GetProperty("Task")!.GetValue(countdownInfo)!;
    }

    /// <summary>
    /// Locates the (private) countdown info for the given countdown inside the
    /// room's tracked countdowns.
    /// </summary>
    /// <param name="room">The room hosting the countdown.</param>
    /// <param name="countdown">The countdown to locate.</param>
    /// <returns>The countdown info instance.</returns>
    private static object FindCountdownInfo(RoomBase room, MultiplayerCountdown countdown)
    {
        FieldInfo trackedCountdownsField = typeof(RoomBase).GetField("_trackedCountdowns", BindingFlags.NonPublic | BindingFlags.Instance)
                                           ?? throw new InvalidOperationException("Could not find RoomBase._trackedCountdowns field.");

        // ConcurrentDictionary<MultiplayerCountdown, CountdownInfo> implements the
        // non-generic IEnumerable; each item is a boxed KeyValuePair whose Key is
        // the countdown and whose Value is the (private) CountdownInfo instance.
        IEnumerable trackedCountdowns = (IEnumerable)trackedCountdownsField.GetValue(room)!;

        foreach (object item in trackedCountdowns)
        {
            Type itemType = item.GetType();
            if (!ReferenceEquals(itemType.GetProperty("Key")!.GetValue(item), countdown))
            {
                continue;
            }

            return itemType.GetProperty("Value")!.GetValue(item)!;
        }

        throw new InvalidOperationException("Could not find the tracked countdown.");
    }

    public class ExclusiveCountdown : MultiplayerCountdown
    {
        public override bool IsExclusive => true;
    }
}