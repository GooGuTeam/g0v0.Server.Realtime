// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Online.Metadata;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Tests.Hubs;

[TestFixture]
public class MetadataHubTests
{
    [Test]
    public async Task BeginWatchingUserPresence_ReplaysVisibleUsersAndAddsCallerToWatcherGroup()
    {
        var environment = new MetadataHubTestEnvironment();

        TestHubHandle onlineUser = await environment.ConnectAsync(10, "connection-online");
        await onlineUser.Hub.UpdateStatus(UserStatus.Online);

        TestHubHandle offlineUser = await environment.ConnectAsync(20, "connection-offline");
        await offlineUser.Hub.UpdateStatus(UserStatus.Offline);

        TestHubHandle watcher = await environment.ConnectAsync(30, "connection-watcher");
        watcher.Recorder.Clear();
        watcher.GroupManager.Reset();

        await watcher.Hub.BeginWatchingUserPresence();

        Assert.Multiple(() =>
        {
            Assert.That(watcher.Recorder.UserPresenceUpdates, Has.Count.EqualTo(1));
            Assert.That(watcher.Recorder.UserPresenceUpdates[0].UserId, Is.EqualTo(10));
            Assert.That(watcher.Recorder.UserPresenceUpdates[0].Presence, Is.Not.Null);
            Assert.That(
                watcher.GroupManager.AddedGroups,
                Is.EqualTo([(watcher.ConnectionId, MetadataHub.OnlinePresenceWatchersGroup)]));
        });
    }

    [Test]
    public async Task RefreshFriends_ReplacesTrackedGroupsAndPublishesPresenceForNewOnlineFriends()
    {
        var environment = new MetadataHubTestEnvironment();
        environment.RelationshipRepository.SetFriendIds(30, [10]);

        TestHubHandle oldFriend = await environment.ConnectAsync(10, "connection-old-friend");
        await oldFriend.Hub.UpdateStatus(UserStatus.Online);

        TestHubHandle newFriend = await environment.ConnectAsync(20, "connection-new-friend");
        await newFriend.Hub.UpdateStatus(UserStatus.Online);

        TestHubHandle caller = await environment.ConnectAsync(30, "connection-caller");

        environment.RelationshipRepository.SetFriendIds(30, [20]);
        caller.Recorder.Clear();
        caller.GroupManager.Reset();

        await caller.Hub.RefreshFriends();

        Assert.Multiple(() =>
        {
            Assert.That(
                caller.GroupManager.RemovedGroups,
                Is.EqualTo([(caller.ConnectionId, MetadataHub.FriendPresenceWatchersGroup(10))]));
            Assert.That(
                caller.GroupManager.AddedGroups,
                Is.EqualTo([(caller.ConnectionId, MetadataHub.FriendPresenceWatchersGroup(20))]));
            Assert.That(caller.Recorder.FriendPresenceUpdates, Has.Count.EqualTo(1));
            Assert.That(caller.Recorder.FriendPresenceUpdates[0].UserId, Is.EqualTo(20));
            Assert.That(caller.Recorder.FriendPresenceUpdates[0].Presence, Is.Not.Null);
        });
    }

    [Test]
    public async Task RefreshFriends_DoesNotPublishPresenceForOfflineNewFriends()
    {
        var environment = new MetadataHubTestEnvironment();
        TestHubHandle friend = await environment.ConnectAsync(20, "connection-friend");
        await friend.Hub.UpdateStatus(UserStatus.Offline);

        TestHubHandle caller = await environment.ConnectAsync(30, "connection-caller");
        environment.RelationshipRepository.SetFriendIds(30, [20]);
        caller.Recorder.Clear();
        caller.GroupManager.Reset();

        await caller.Hub.RefreshFriends();

        Assert.Multiple(() =>
        {
            Assert.That(caller.Recorder.FriendPresenceUpdates, Is.Empty);
            Assert.That(
                caller.GroupManager.AddedGroups,
                Is.EqualTo([(caller.ConnectionId, MetadataHub.FriendPresenceWatchersGroup(20))]));
        });
    }

    [Test]
    public async Task BroadcastUserPresenceUpdate_WhenPresenceIsVisible_NotifiesWatcherGroupsAndCaller()
    {
        var environment = new MetadataHubTestEnvironment();
        environment.Router.EnsureConnection("caller");
        environment.Router.EnsureConnection("watcher");
        environment.Router.EnsureConnection("friend-watcher");
        environment.Router.AddToGroup("watcher", MetadataHub.OnlinePresenceWatchersGroup);
        environment.Router.AddToGroup("friend-watcher", MetadataHub.FriendPresenceWatchersGroup(42));

        var player = new LazerPlayer(42, new PlayerFacade(environment.PlayerManager));
        player.State.UserStatus = UserStatus.Online;

        await InvokeBroadcastUserPresenceUpdateAsync(environment.HubContext, "caller", player);

        Assert.Multiple(() =>
        {
            Assert.That(environment.Router.GetRecorder("caller").UserPresenceUpdates, Has.Count.EqualTo(1));
            Assert.That(environment.Router.GetRecorder("watcher").UserPresenceUpdates, Has.Count.EqualTo(1));
            Assert.That(environment.Router.GetRecorder("watcher").UserPresenceUpdates[0].UserId, Is.EqualTo(42));
            Assert.That(environment.Router.GetRecorder("friend-watcher").FriendPresenceUpdates, Has.Count.EqualTo(1));
            Assert.That(environment.Router.GetRecorder("friend-watcher").FriendPresenceUpdates[0].UserId, Is.EqualTo(42));
        });
    }

    [Test]
    public async Task BroadcastUserPresenceUpdate_WhenPresenceIsHidden_NotifiesOnlyCaller()
    {
        var environment = new MetadataHubTestEnvironment();
        environment.Router.EnsureConnection("caller");
        environment.Router.EnsureConnection("watcher");
        environment.Router.EnsureConnection("friend-watcher");
        environment.Router.AddToGroup("watcher", MetadataHub.OnlinePresenceWatchersGroup);
        environment.Router.AddToGroup("friend-watcher", MetadataHub.FriendPresenceWatchersGroup(42));

        var player = new LazerPlayer(42, new PlayerFacade(environment.PlayerManager));
        player.State.UserStatus = UserStatus.Offline;

        await InvokeBroadcastUserPresenceUpdateAsync(environment.HubContext, "caller", player);

        Assert.Multiple(() =>
        {
            Assert.That(environment.Router.GetRecorder("caller").UserPresenceUpdates, Has.Count.EqualTo(1));
            Assert.That(environment.Router.GetRecorder("caller").UserPresenceUpdates[0].Presence, Is.Null);
            Assert.That(environment.Router.GetRecorder("watcher").UserPresenceUpdates, Is.Empty);
            Assert.That(environment.Router.GetRecorder("friend-watcher").FriendPresenceUpdates, Is.Empty);
        });
    }

    [TestCase(null, false)]
    [TestCase(UserStatus.Offline, false)]
    [TestCase(UserStatus.Online, true)]
    [TestCase(UserStatus.DoNotDisturb, true)]
    public void ShouldBroadcastPresenceToOtherUsers_ReturnsExpectedValue(UserStatus? status, bool expected)
    {
        var state = new PlayerState(new IdleActivity())
        {
            UserStatus = status,
        };

        bool result = MetadataHub.ShouldBroadcastPresenceToOtherUsers(state);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ShouldBroadcastPresenceToOtherUsers_WhenStatusIsUnexpected_ThrowsArgumentOutOfRangeException()
    {
        var state = new PlayerState(new IdleActivity())
        {
            UserStatus = (UserStatus)999,
        };

        Assert.That(
            () => MetadataHub.ShouldBroadcastPresenceToOtherUsers(state),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void FriendPresenceWatchersGroup_ReturnsExpectedGroupName()
    {
        Assert.That(
            MetadataHub.FriendPresenceWatchersGroup(42),
            Is.EqualTo("metadata:online-presence-watchers:42"));
    }

    private static async Task InvokeBroadcastUserPresenceUpdateAsync(
        IHubContext<MetadataHub, IMetadataClient> hubContext,
        string connectionId,
        IPlayer player)
    {
        MethodInfo method = typeof(MetadataHub).GetMethod(
                "BroadcastUserPresenceUpdate",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not locate BroadcastUserPresenceUpdate.");

        var task = (Task?)method.Invoke(null, [hubContext, connectionId, player]);
        await (task ?? Task.CompletedTask);
    }

    private sealed class MetadataHubTestEnvironment
    {
        public MetadataHubTestEnvironment()
        {
            IpcClient = new InterProcessCommunicationClient(new RecordingTransport(), "realtime-tests");
            PlayerManager = new PlayerManager(NullLogger<PlayerManager>.Instance, IpcClient);
            Router = new MetadataClientRouter();
            RelationshipRepository = new FakeRelationshipRepository();
            HubContext = new FakeHubContext(Router);
        }

        public FakeHubContext HubContext { get; }

        public InterProcessCommunicationClient IpcClient { get; }

        public PlayerManager PlayerManager { get; }

        public FakeRelationshipRepository RelationshipRepository { get; }

        public MetadataClientRouter Router { get; }

        public async Task<TestHubHandle> ConnectAsync(int userId, string connectionId)
        {
            Router.EnsureConnection(connectionId);

            var groupManager = new FakeGroupManager(Router);
            var hub = new MetadataHub(PlayerManager, RelationshipRepository, HubContext)
            {
                Context = new TestHubCallerContext(connectionId, userId.ToString()),
                Clients = new FakeHubCallerClients(Router, connectionId),
                Groups = groupManager,
            };

            await hub.OnConnectedAsync();

            var handle = new TestHubHandle(hub, connectionId, Router.GetRecorder(connectionId), groupManager);
            handle.Recorder.Clear();
            handle.GroupManager.Reset();
            return handle;
        }
    }

    private sealed record TestHubHandle(
        MetadataHub Hub,
        string ConnectionId,
        RecordingMetadataClient Recorder,
        FakeGroupManager GroupManager);

    private sealed class RecordingTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }

    private sealed class FakeRelationshipRepository : IRelationshipRepository
    {
        private readonly Dictionary<long, int[]> _friendIds = new();

        public void SetFriendIds(long userId, int[] friendIds) => _friendIds[userId] = friendIds;

        public Task<int[]> GetAllFriendIds(long userId)
            => Task.FromResult(_friendIds.TryGetValue(userId, out int[]? ids) ? ids : []);

        public Task<IReadOnlyList<Relationship>> GetByUserIdAsync(long userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<Relationship>> GetByTargetIdAsync(long targetId) => throw new NotSupportedException();

        public Task<Relationship?> GetRelationshipAsync(long userId, long targetId) => throw new NotSupportedException();

        public Task<bool> IsFollowingAsync(long userId, long targetId) => throw new NotSupportedException();

        public Task<bool> IsBlockedAsync(long userId, long targetId) => throw new NotSupportedException();

        public Task CreateAsync(Relationship relationship) => throw new NotSupportedException();

        public Task UpdateAsync(Relationship relationship) => throw new NotSupportedException();

        public Task DeleteAsync(Relationship relationship) => throw new NotSupportedException();
    }

    private sealed class TestHubCallerContext(string connectionId, string userIdentifier) : HubCallerContext
    {
        private readonly FeatureCollection _features = new();
        private readonly Dictionary<object, object?> _items = [];

        public override string ConnectionId => connectionId;

        public override string UserIdentifier => userIdentifier;

        public override System.Security.Claims.ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items => _items;

        public override IFeatureCollection Features => _features;

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }

    private sealed class FakeHubContext(MetadataClientRouter router) : IHubContext<MetadataHub, IMetadataClient>
    {
        public IHubClients<IMetadataClient> Clients { get; } = new FakeHubClients(router);

        public IGroupManager Groups { get; } = new FakeGroupManager(router);
    }

    private sealed class FakeHubCallerClients(MetadataClientRouter router, string callerConnectionId) : IHubCallerClients<IMetadataClient>
    {
        public IMetadataClient All => router.CreateProxy(static state => state.ConnectionIds);

        public IMetadataClient Caller => router.CreateProxy(_ => [callerConnectionId]);

        public IMetadataClient Others => router.CreateProxy(state => state.ConnectionIds.Where(id => id != callerConnectionId));

        public IMetadataClient OthersInGroup(string groupName) =>
            router.CreateProxy(state => state.GetGroupMembers(groupName).Where(id => id != callerConnectionId));

        public IMetadataClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            router.CreateProxy(state => state.ConnectionIds.Except(excludedConnectionIds, StringComparer.Ordinal));

        public IMetadataClient Client(string connectionId) => router.CreateProxy(_ => [connectionId]);

        public IMetadataClient Clients(IReadOnlyList<string> connectionIds) => router.CreateProxy(_ => connectionIds);

        public IMetadataClient Group(string groupName) => router.CreateProxy(state => state.GetGroupMembers(groupName));

        public IMetadataClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            router.CreateProxy(state => state.GetGroupMembers(groupName).Except(excludedConnectionIds, StringComparer.Ordinal));

        public IMetadataClient Groups(IReadOnlyList<string> groupNames) =>
            router.CreateProxy(state => state.GetGroupMembers(groupNames));

        public IMetadataClient User(string userId) => throw new NotSupportedException();

        public IMetadataClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeHubClients(MetadataClientRouter router) : IHubClients<IMetadataClient>
    {
        public IMetadataClient All => router.CreateProxy(static state => state.ConnectionIds);

        public IMetadataClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            router.CreateProxy(state => state.ConnectionIds.Except(excludedConnectionIds, StringComparer.Ordinal));

        public IMetadataClient Client(string connectionId) => router.CreateProxy(_ => [connectionId]);

        public IMetadataClient Clients(IReadOnlyList<string> connectionIds) => router.CreateProxy(_ => connectionIds);

        public IMetadataClient Group(string groupName) => router.CreateProxy(state => state.GetGroupMembers(groupName));

        public IMetadataClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            router.CreateProxy(state => state.GetGroupMembers(groupName).Except(excludedConnectionIds, StringComparer.Ordinal));

        public IMetadataClient Groups(IReadOnlyList<string> groupNames) =>
            router.CreateProxy(state => state.GetGroupMembers(groupNames));

        public IMetadataClient User(string userId) => throw new NotSupportedException();

        public IMetadataClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeGroupManager(MetadataClientRouter router) : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> AddedGroups { get; } = [];

        public List<(string ConnectionId, string GroupName)> RemovedGroups { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddedGroups.Add((connectionId, groupName));
            router.AddToGroup(connectionId, groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            RemovedGroups.Add((connectionId, groupName));
            router.RemoveFromGroup(connectionId, groupName);
            return Task.CompletedTask;
        }

        public void Reset()
        {
            AddedGroups.Clear();
            RemovedGroups.Clear();
        }
    }

    private sealed class MetadataClientRouter
    {
        private readonly Dictionary<string, RecordingMetadataClient> _connections = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _groups = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> ConnectionIds => _connections.Keys;

        public void AddToGroup(string connectionId, string groupName)
        {
            EnsureConnection(connectionId);
            if (!_groups.TryGetValue(groupName, out HashSet<string>? members))
            {
                members = new HashSet<string>(StringComparer.Ordinal);
                _groups[groupName] = members;
            }

            members.Add(connectionId);
        }

        public IMetadataClient CreateProxy(Func<MetadataClientRouter, IEnumerable<string>> targetSelector)
            => new RoutedMetadataClient(this, targetSelector);

        public void EnsureConnection(string connectionId)
        {
            if (!_connections.ContainsKey(connectionId))
            {
                _connections[connectionId] = new RecordingMetadataClient();
            }
        }

        public RecordingMetadataClient GetRecorder(string connectionId)
        {
            EnsureConnection(connectionId);
            return _connections[connectionId];
        }

        public string[] GetGroupMembers(string groupName)
            => _groups.TryGetValue(groupName, out HashSet<string>? members) ? members.ToArray() : [];

        public string[] GetGroupMembers(IEnumerable<string> groupNames)
            => groupNames.SelectMany(GetGroupMembers).Distinct(StringComparer.Ordinal).ToArray();

        public void RemoveFromGroup(string connectionId, string groupName)
        {
            if (_groups.TryGetValue(groupName, out HashSet<string>? members))
            {
                members.Remove(connectionId);
            }
        }

        private sealed class RoutedMetadataClient(
            MetadataClientRouter router,
            Func<MetadataClientRouter, IEnumerable<string>> targetSelector) : IMetadataClient
        {
            public Task BeatmapSetsUpdated(BeatmapUpdates beatmapUpdates)
                => Task.WhenAll(Targets().Select(target => target.BeatmapSetsUpdated(beatmapUpdates)));

            public Task DisconnectRequested()
                => Task.WhenAll(Targets().Select(static target => target.DisconnectRequested()));

            public Task DailyChallengeUpdated(DailyChallengeInfo? dailyChallengeInfo)
                => Task.WhenAll(Targets().Select(target => target.DailyChallengeUpdated(dailyChallengeInfo)));

            public Task FriendPresenceUpdated(int userId, UserPresence? userPresence)
                => Task.WhenAll(Targets().Select(target => target.FriendPresenceUpdated(userId, userPresence)));

            public Task MultiplayerRoomScoreSet(MultiplayerRoomScoreSetEvent multiplayerRoomScoreSetEvent)
                => Task.WhenAll(Targets().Select(target => target.MultiplayerRoomScoreSet(multiplayerRoomScoreSetEvent)));

            public Task UserPresenceUpdated(int userId, UserPresence? userPresence)
                => Task.WhenAll(Targets().Select(target => target.UserPresenceUpdated(userId, userPresence)));

            private IEnumerable<RecordingMetadataClient> Targets()
                => targetSelector(router).Distinct(StringComparer.Ordinal).Select(router.GetRecorder);
        }
    }

    private sealed class RecordingMetadataClient : IMetadataClient
    {
        public List<(int UserId, UserPresence? Presence)> FriendPresenceUpdates { get; } = [];

        public List<(int UserId, UserPresence? Presence)> UserPresenceUpdates { get; } = [];

        public Task BeatmapSetsUpdated(BeatmapUpdates beatmapUpdates) => Task.CompletedTask;

        public void Clear()
        {
            FriendPresenceUpdates.Clear();
            UserPresenceUpdates.Clear();
        }

        public Task DisconnectRequested() => Task.CompletedTask;

        public Task DailyChallengeUpdated(DailyChallengeInfo? dailyChallengeInfo) => Task.CompletedTask;

        public Task FriendPresenceUpdated(int userId, UserPresence? userPresence)
        {
            FriendPresenceUpdates.Add((userId, userPresence));
            return Task.CompletedTask;
        }

        public Task MultiplayerRoomScoreSet(MultiplayerRoomScoreSetEvent multiplayerRoomScoreSetEvent)
            => Task.CompletedTask;

        public Task UserPresenceUpdated(int userId, UserPresence? userPresence)
        {
            UserPresenceUpdates.Add((userId, userPresence));
            return Task.CompletedTask;
        }
    }
}