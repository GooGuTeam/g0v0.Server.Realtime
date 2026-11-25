// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Game.Online.Spectator;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Tests.Manager;

[TestFixture]
public class PlayerManagerTests
{
    [Test]
    public async Task AddPlayer_WhenPlayerIsOffline_DoesNotBroadcastOnlinePresence()
    {
        PlayerManager manager = CreateManager();
        TestPlayer observer = new(2, "test", manager, UserStatus.Online);
        TestPlayer offlinePlayer = new(1, "test", manager, UserStatus.Offline);

        await observer.Online();
        observer.ResetNotifications();

        await offlinePlayer.Online();

        Assert.That(observer.OnlineNotifications, Is.Empty);
    }

    [Test]
    public async Task AddPlayer_WhenSameServerPlayerExists_KicksExistingInstanceAndKeepsReplacement()
    {
        PlayerManager manager = CreateManager();
        TestPlayer observer = new(2, "test", manager, UserStatus.Online);
        TestPlayer first = new(1, "test", manager, UserStatus.Online);
        TestPlayer second = new(1, "test", manager, UserStatus.Online);

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
    public async Task AddPlayer_WhenReplacingLazerPlayer_RequestsExistingConnectionsDisconnect()
    {
        PlayerManager manager = CreateManager();
        LazerPlayer first = new(1, new PlayerFacade(manager));
        LazerPlayer second = new(1, new PlayerFacade(manager));
        int firstDisconnectRequests = 0;
        int secondDisconnectRequests = 0;
        first.DisconnectMetadataHubConnection = connectionId =>
        {
            Assert.That(connectionId, Is.EqualTo("metadata-old"));
            firstDisconnectRequests++;
            return Task.CompletedTask;
        };
        second.DisconnectMetadataHubConnection = _ =>
        {
            secondDisconnectRequests++;
            return Task.CompletedTask;
        };

        await first.HubConnected("MetadataHub", "metadata-old");
        await second.HubConnected("MetadataHub", "metadata-new");

        Assert.Multiple(() =>
        {
            Assert.That(manager.GetPlayer<LazerPlayer>(1, "Lazer"), Is.SameAs(second));
            Assert.That(firstDisconnectRequests, Is.EqualTo(1));
            Assert.That(secondDisconnectRequests, Is.Zero);
        });
    }

    [Test]
    public async Task GetOrCreatePlayer_WhenMultipleHubsConnect_ReusesPendingPlayerWithoutKicking()
    {
        PlayerManager manager = CreateManager();
        TestPlayer observer = new(2, "test", manager, UserStatus.Online);
        await observer.Online();
        observer.ResetNotifications();

        PlayerFacade metadataFacade = new(manager);
        PlayerFacade spectatorFacade = new(manager);
        LazerPlayer metadataPlayer = manager.GetOrCreatePlayer<LazerPlayer>(
            1,
            "Lazer",
            () => new LazerPlayer(1, metadataFacade),
            player => player.Facade.ApplyNonNullDependenciesFrom(metadataFacade));
        LazerPlayer spectatorPlayer = manager.GetOrCreatePlayer<LazerPlayer>(
            1,
            "Lazer",
            () => new LazerPlayer(1, spectatorFacade),
            player => player.Facade.ApplyNonNullDependenciesFrom(spectatorFacade));

        await Task.WhenAll(
            metadataPlayer.HubConnected("MetadataHub", "metadata-connection"),
            spectatorPlayer.HubConnected("SpectatorHub", "spectator-connection"));

        Assert.Multiple(() =>
        {
            Assert.That(spectatorPlayer, Is.SameAs(metadataPlayer));
            Assert.That(manager.GetPlayer<LazerPlayer>(1), Is.SameAs(metadataPlayer));
            Assert.That(observer.OnlineNotifications, Is.EqualTo([1]));
            Assert.That(observer.OfflineNotifications, Is.Empty);
        });
    }

    [Test]
    public async Task ChangePlayerStatusAsync_BroadcastsUpdatedStatusToOtherPlayers()
    {
        PlayerManager manager = CreateManager();
        TestPlayer observer = new(2, "test", manager, UserStatus.Online);
        TestPlayer subject = new(1, "test", manager, UserStatus.Online);

        await observer.Online();
        await subject.Online();
        observer.ResetNotifications();

        await subject.ChangePlayerStatusAsync(UserStatus.DoNotDisturb);

        Assert.That(observer.StatusNotifications, Is.EqualTo([(1, UserStatus.DoNotDisturb)]));
    }

    [Test]
    public async Task UserWatchingPlayer_WhenTargetIsPlaying_ReplaysBeginPlayingToWatcher()
    {
        PlayerManager manager = CreateManager();
        TestPlayer watcher = new(2, "test", manager, UserStatus.Online);
        TestPlayer target = new(1, "test", manager, UserStatus.Online);
        target.State.SpectatorState = new SpectatorState
        {
            State = SpectatedUserState.Playing,
        };

        await manager.UserWatchingPlayer(watcher, target);

        Assert.That(watcher.BeganPlayingNotifications, Is.EqualTo([1]));
    }

    [Test]
    public async Task UserStoppedWatchingPlayer_WhenWatcherWasRegistered_NotifiesTarget()
    {
        PlayerManager manager = CreateManager();
        TestPlayer watcher = new(2, "test", manager, UserStatus.Online);
        TestPlayer target = new(1, "test", manager, UserStatus.Online);
        await manager.UserWatchingPlayer(watcher, target);

        await manager.UserStoppedWatchingPlayer(watcher, target);

        Assert.That(target.WatchedStoppedNotifications, Is.EqualTo([2]));
    }

    [Test]
    public async Task PresenceNotices_WhenReplacementConnectsDuringOfflinePublish_LastNoticeStaysOnline()
    {
        TaskCompletionSource offlinePublishStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseOfflinePublish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        RecordingTransport transport = new()
        {
            BeforePublishAsync = async index =>
            {
                // 1: initial AddPlayer online notice
                // 2: RemovePlayer offline notice (held so a replacement can register mid-publish)
                if (index == 2)
                {
                    offlinePublishStarted.TrySetResult();
                    await releaseOfflinePublish.Task;
                }
            },
        };

        PlayerManager manager = CreateManager(transport);
        TestPlayer first = new(1, "test", manager, UserStatus.Online);
        TestPlayer replacement = new(1, "test", manager, UserStatus.Online);

        await first.Online();

        Task offlineTask = first.Offline();
        await offlinePublishStarted.Task;

        Task onlineTask = replacement.Online();
        Assert.That(manager.GetPlayer<TestPlayer>(1, "test"), Is.SameAs(replacement));

        releaseOfflinePublish.TrySetResult();
        await Task.WhenAll(offlineTask, onlineTask);

        List<bool> presence = GetPresenceNotices(transport.Payloads);
        Assert.Multiple(() =>
        {
            Assert.That(presence, Is.Not.Empty);
            Assert.That(presence[^1], Is.True);
            Assert.That(manager.GetPlayer<TestPlayer>(1, "test"), Is.SameAs(replacement));
        });
    }

    private static PlayerManager CreateManager() => CreateManager(new NoOpTransport());

    private static PlayerManager CreateManager(IInterProcessCommunicationTransport transport) =>
        new(
            NullLogger<PlayerManager>.Instance,
            new InterProcessCommunicationClient(transport, "realtime-tests"));

    private static List<bool> GetPresenceNotices(IEnumerable<string> payloads)
    {
        List<bool> notices = [];
        foreach (string payload in payloads)
        {
            IpcMessage? message = JsonConvert.DeserializeObject<IpcMessage>(payload);
            if (message is not { Name: "user_online_status_changed", Data: JObject data })
            {
                continue;
            }

            if (data.TryGetValue("is_online", out JToken? isOnline))
            {
                notices.Add(isOnline.Value<bool>());
            }
        }

        return notices;
    }

    private sealed class TestPlayer : PlayerBase
    {
        public TestPlayer(int playerId, string server, PlayerManager manager, UserStatus initialStatus)
            : base(
                playerId,
                new PlayerFacade(manager),
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

        public List<(int PlayerId, UserStatus? Status)> StatusNotifications { get; } = [];

        public List<int> BeganPlayingNotifications { get; } = [];

        public List<int> WatchedStoppedNotifications { get; } = [];

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
            return Task.CompletedTask;
        }

        public override Task OnPlayerChangeStatus(IPlayer player, UserStatus? status)
        {
            StatusNotifications.Add((player.PlayerId, status));
            return Task.CompletedTask;
        }

        public override Task OnUserBeganPlaying(IPlayer player)
        {
            BeganPlayingNotifications.Add(player.PlayerId);
            return Task.CompletedTask;
        }

        public override Task OnUserFinishedPlaying(IPlayer player) => Task.CompletedTask;

        public override Task OnUserSentFrames(IPlayer player, FrameDataBundle frame) => Task.CompletedTask;

        public override Task OnWatched(IPlayer source) => Task.CompletedTask;

        public override Task OnWatchedStopped(IPlayer source)
        {
            WatchedStoppedNotifications.Add(source.PlayerId);
            return Task.CompletedTask;
        }

        public override Task OnScoreProcessed(long scoreId) => Task.CompletedTask;

        public void ResetNotifications()
        {
            OnlineNotifications.Clear();
            OfflineNotifications.Clear();
            StatusNotifications.Clear();
            BeganPlayingNotifications.Clear();
            WatchedStoppedNotifications.Clear();
        }
    }

    private sealed class NoOpTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }

    private sealed class RecordingTransport : IInterProcessCommunicationTransport
    {
        private readonly List<string> _payloads = [];
        private readonly Lock _sync = new();
        private int _publishCount;

        public Func<int, Task>? BeforePublishAsync { get; init; }

        public IReadOnlyList<string> Payloads
        {
            get
            {
                lock (_sync)
                {
                    return [.. _payloads];
                }
            }
        }

        public async Task PublishAsync(string channel, string payload)
        {
            int index = Interlocked.Increment(ref _publishCount);
            if (BeforePublishAsync is not null)
            {
                await BeforePublishAsync(index);
            }

            lock (_sync)
            {
                _payloads.Add(payload);
            }
        }

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }
}