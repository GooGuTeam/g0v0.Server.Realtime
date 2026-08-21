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
/// Tests for the server-managed force-start countdown which begins when a match
/// starts, ported from osu-server-spectator's <c>AutomaticForceStartTest</c>.
/// </summary>
[TestFixture]
public class AutomaticForceStartTest : MultiplayerTestBase
{
    [Test]
    public async Task CountdownStartsWhenMatchStarts()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        await Hub.StartMatch();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.ActiveCountdowns.OfType<ForceGameplayStartCountdown>().Any(), Is.True);
    }

    [Test]
    public async Task CountdownStopsWhenAllPlayersAbort()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        await Hub.StartMatch();
        await Hub.AbortGameplay();

        var room = Hub.GetRoom(ROOMID);
        Assert.That(room, Is.Not.Null);
        Assert.That(room!.ActiveCountdowns.OfType<ForceGameplayStartCountdown>().Any(), Is.False);
    }

    [Test]
    public async Task LoadingUsersAbortWhenCountdownEnds()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        await SkipToEndOfCountdown();

        var room = Hub.GetRoom(ROOMID)!;
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Open));
        Assert.That(room.Users.Single(u => u.UserID == USERID).State, Is.EqualTo(MultiplayerUserState.Idle));

        UserReceiver.Verify(r => r.GameplayAborted(It.Is<GameplayAbortReason>(reason => reason == GameplayAbortReason.LoadTookTooLong)), Times.Once);
        UserReceiver.Verify(r => r.GameplayStarted(), Times.Never);
    }

    [Test]
    public async Task LoadedUsersStartWhenCountdownEnds()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();
        await Hub.ChangeState(MultiplayerUserState.Loaded);

        await SkipToEndOfCountdown();

        var room = Hub.GetRoom(ROOMID)!;
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Playing));
        Assert.That(room.Users.Single(u => u.UserID == USERID).State, Is.EqualTo(MultiplayerUserState.Playing));

        UserReceiver.Verify(r => r.GameplayAborted(It.Is<GameplayAbortReason>(reason => reason == GameplayAbortReason.LoadTookTooLong)), Times.Never);
        UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);
    }

    [Test]
    public async Task ReadyAndLoadedUsersStartWhenCountdownEnds()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        SetUserContext(ContextUser2);
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();

        // User 1 becomes ready for gameplay.
        SetUserContext(ContextUser);
        await Hub.StartMatch();
        await Hub.ChangeState(MultiplayerUserState.Loaded);
        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

        // User 2 becomes loaded.
        SetUserContext(ContextUser2);
        await Hub.ChangeState(MultiplayerUserState.Loaded);

        await SkipToEndOfCountdown();

        var room = Hub.GetRoom(ROOMID)!;
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.Playing));
        Assert.That(room.Users.Single(u => u.UserID == USERID).State, Is.EqualTo(MultiplayerUserState.Playing));
        Assert.That(room.Users.Single(u => u.UserID == USERID2).State, Is.EqualTo(MultiplayerUserState.Playing));

        UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);
        User2Receiver.Verify(r => r.GameplayStarted(), Times.Once);
    }

    [Test]
    public async Task CountdownCannotBeStopped()
    {
        await Hub.JoinRoom(ROOMID);
        await MarkCurrentUserReadyAndAvailable();
        await Hub.StartMatch();

        int countdownId = Hub.GetRoom(ROOMID)!.ActiveCountdowns.OfType<ForceGameplayStartCountdown>().Single().ID;

        Assert.That(
            async () => await Hub.SendMatchRequest(new StopCountdownRequest(countdownId)),
            Throws.TypeOf<InvalidStateException>());

        var room = Hub.GetRoom(ROOMID)!;
        Assert.That(room.ActiveCountdowns.OfType<ForceGameplayStartCountdown>().Any(), Is.True);
    }

    /// <summary>
    /// Skips to the end of the active <see cref="ForceGameplayStartCountdown"/> and
    /// runs its callback as soon as possible unless the countdown has been cancelled.
    /// </summary>
    /// <remarks>
    /// g0v0's <see cref="RoomBase"/> does not expose the reference implementation's
    /// <c>SkipToEndOfCountdown</c> helper, so the countdown's internal skip token is
    /// cancelled via reflection (equivalent to cancelling <c>CountdownInfo.SkipSource</c>).
    /// </remarks>
    /// <returns>A task which completes when the active countdown completes.</returns>
    private async Task SkipToEndOfCountdown()
    {
        var room = Hub.GetRoom(ROOMID)!;

        var trackedCountdownsField = typeof(RoomBase).GetField("trackedCountdowns", BindingFlags.NonPublic | BindingFlags.Instance)
                                     ?? throw new InvalidOperationException("Could not find RoomBase.trackedCountdowns field.");

        // ConcurrentDictionary<MultiplayerCountdown, CountdownInfo> implements the
        // non-generic IEnumerable; each item is a boxed KeyValuePair whose Key is
        // the countdown and whose Value is the (private) CountdownInfo instance.
        var trackedCountdowns = (IEnumerable)trackedCountdownsField.GetValue(room)!;

        foreach (object item in trackedCountdowns)
        {
            Type itemType = item.GetType();
            var countdown = (MultiplayerCountdown)itemType.GetProperty("Key")!.GetValue(item)!;

            if (countdown is not ForceGameplayStartCountdown)
            {
                continue;
            }

            object countdownInfo = itemType.GetProperty("Value")!.GetValue(item)!;
            Type countdownInfoType = countdownInfo.GetType();

            var skipSource = (CancellationTokenSource)countdownInfoType.GetProperty("SkipSource")!.GetValue(countdownInfo)!;
            var countdownTask = (Task)countdownInfoType.GetProperty("Task")!.GetValue(countdownInfo)!;

            skipSource.Cancel();

            try
            {
                await countdownTask;
            }
            catch (TaskCanceledException)
            {
                // don't care if task was cancelled.
            }

            return;
        }
    }
}