// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Tests.Manager;

[TestFixture]
public class PlayerManagerTests
{
    [Test]
    public async Task AddPlayer_WhenPlayerIsOffline_DoesNotBroadcastOnlinePresence()
    {
        var manager = CreateManager();
        var observer = new TestPlayer(2, "test", manager, UserStatus.Online);
        var offlinePlayer = new TestPlayer(1, "test", manager, UserStatus.Offline);

        await observer.Online();
        observer.ResetNotifications();

        await offlinePlayer.Online();

        Assert.That(observer.OnlineNotifications, Is.Empty);
    }

    [Test]
    public async Task AddPlayer_WhenSameServerPlayerExists_KicksExistingInstanceAndKeepsReplacement()
    {
        var manager = CreateManager();
        var observer = new TestPlayer(2, "test", manager, UserStatus.Online);
        var first = new TestPlayer(1, "test", manager, UserStatus.Online);
        var second = new TestPlayer(1, "test", manager, UserStatus.Online);

        await observer.Online();
        await first.Online();
        observer.ResetNotifications();

        await second.Online();

        Assert.Multiple(() =>
        {
            Assert.That(manager.GetPlayer<TestPlayer>(1, "test"), Is.SameAs(second));
            Assert.That(observer.OfflineNotifications, Has.Count.EqualTo(1));
            Assert.That(observer.OfflineNotifications[0].PlayerId, Is.EqualTo(1));
            Assert.That(observer.OfflineNotifications[0].IsKicked, Is.True);
            Assert.That(observer.OnlineNotifications, Is.EqualTo([1]));
        });
    }

    [Test]
    public async Task ChangePlayerStatusAsync_BroadcastsUpdatedStatusToOtherPlayers()
    {
        var manager = CreateManager();
        var observer = new TestPlayer(2, "test", manager, UserStatus.Online);
        var subject = new TestPlayer(1, "test", manager, UserStatus.Online);

        await observer.Online();
        await subject.Online();
        observer.ResetNotifications();

        await subject.ChangePlayerStatusAsync(UserStatus.DoNotDisturb);

        Assert.That(observer.StatusNotifications, Is.EqualTo([(1, UserStatus.DoNotDisturb)]));
    }

    private static PlayerManager CreateManager() => new(NullLogger<PlayerManager>.Instance);

    private sealed class TestPlayer : PlayerBase
    {
        public TestPlayer(int playerId, string server, PlayerManager manager, UserStatus initialStatus)
            : base(
                playerId,
                manager,
                new PlayerState(new IdleActivity())
                {
                    UserStatus = initialStatus,
                })
        {
            Server = server;
        }

        public override string Server { get; }

        public List<int> OnlineNotifications { get; } = [];

        public List<(int PlayerId, bool IsKicked)> OfflineNotifications { get; } = [];

        public List<(int PlayerId, IUserActivity Activity)> ActivityNotifications { get; } = [];

        public List<(int PlayerId, UserStatus? Status)> StatusNotifications { get; } = [];

        public override Task OnPlayerOnline(IPlayer player)
        {
            OnlineNotifications.Add(player.PlayerId);
            return Task.CompletedTask;
        }

        public override Task OnPlayerOffline(IPlayer player, bool isKicked = false)
        {
            OfflineNotifications.Add((player.PlayerId, isKicked));
            return Task.CompletedTask;
        }

        public override Task OnPlayerChangeActivity(IPlayer player, IUserActivity activity)
        {
            ActivityNotifications.Add((player.PlayerId, activity));
            return Task.CompletedTask;
        }

        public override Task OnPlayerChangeStatus(IPlayer player, UserStatus? status)
        {
            StatusNotifications.Add((player.PlayerId, status));
            return Task.CompletedTask;
        }

        public void ResetNotifications()
        {
            OnlineNotifications.Clear();
            OfflineNotifications.Clear();
            ActivityNotifications.Clear();
            StatusNotifications.Clear();
        }
    }
}
